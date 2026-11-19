"""Independently inspect all public Core archives and their nested installer payload."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import sys
import zipfile
import xml.etree.ElementTree as ET

SIGNATURE=bytes.fromhex('94f0b17b6893e02937eb34ef53aae7d42b54f5707ef5d6f57854983e5e94ed7d')
PRODUCT_CODE={'SonoHUD.exe','SonoHUD.dll','SonoHUD.Core.dll','SonoHUD.PluginContracts.dll','Velopack.dll','YamlDotNet.dll'}
# Negative filenames need not occur in production code or notices. Assembly references
# are also read from PE metadata by M65RAudit (build-time guard names occur legitimately in source).
FORBIDDEN_FILES={'microsoft.windows.sdk.net.dll','winrt.runtime.dll','settings.v2.json','settings.v2.backup.json','scenes.json'}
BLOCK_EXTS={'.pem','.key','.pfx','.p12','.snk','.pdb','.lrc','.mp3','.wav','.flac','.jsonl','.log','.dmp'}
SECRET=re.compile(rb'gh[pousr]_[A-Za-z0-9]{35,}|github_pat_[A-Za-z0-9_]{35,}|-----BEGIN (?:OPENSSH |ENCRYPTED |RSA |EC |DSA )?PRIVATE KEY-----|AKIA[A-Z0-9]{16}')
deny = ()
private_hashes = set()

def apply_exclusions(words, hashes=()):
    global deny, private_hashes
    variants = {v.lower() for v in words if v} | {v.lower().replace(chr(92), '/') for v in words if v}
    deny = tuple((s.encode(), s.encode('utf-16-le')) for s in variants)
    private_hashes = {v.lower() for v in hashes}
    if any(not re.fullmatch('[0-9a-f]{64}', value) for value in private_hashes):
        raise ValueError('제외 파일 지문 형식 오류')

def sha(data):return hashlib.sha256(data).hexdigest()
def clean_name(name):
    path=PurePosixPath(name)
    if not name or '\\' in name or ':' in name or name.startswith('/') or set(name.split('/'))&{'','..','.'}:
        raise ValueError('잘못된 내부 경로: '+name)
    if set(x.lower() for x in path.parts)&{'.git','bin','obj','evidence','appdata'} or path.name.lower() in FORBIDDEN_FILES or path.suffix.lower() in BLOCK_EXTS:
        raise ValueError('공개 제외 파일: '+name)
def inspect_data(name,data):
    if private_hashes and sha(data) in private_hashes:raise ValueError('공개 제외 파일과 동일한 내용: '+name)
    lowered=data.lower()
    for utf8,utf16 in deny:
        if utf8 in lowered or utf16 in lowered:raise ValueError('비공개 공급자/개인 경로 표현: '+name)
    if SECRET.search(data): raise ValueError('자격 증명 형태가 발견된 파일: '+name)
def entries(data,label):
    result={}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for member in archive.infolist():
            name=member.filename.rstrip('/')
            if not name:continue
            clean_name(name)
            if stat.S_ISLNK(member.external_attr>>16) or member.flag_bits&1:raise ValueError('링크 또는 암호화 파일: '+name)
            if member.is_dir():continue
            if name.casefold() in result:raise ValueError('중복 파일: '+name)
            if member.file_size>180_000_000:raise ValueError('검토 크기를 초과한 파일: '+name)
            raw=archive.read(member)
            inspect_data(f'{label}/{name}',raw)
            if raw.startswith(b'PK\x03\x04'):entries(raw,label+'/'+name)
            result[name.casefold()]=(name,raw)
    return result
def pe_code(data):
    if data[:2]!=b'MZ':raise ValueError('실행 파일이 아닙니다.')
    p=struct.unpack_from('<I',data,0x3c)[0];assert data[p:p+4]==b'PE\0\0'
    machine,count=struct.unpack_from('<HH',data,p+4)
    size=struct.unpack_from('<H',data,p+20)[0]
    if machine!=0x8664:raise ValueError('설치 도우미 아키텍처 불일치')
    offset=p+24+size
    for n in range(count):
        start=offset+n*40
        if data[start:start+8].rstrip(b'\0')==b'.text':
            vs=struct.unpack_from('<I',data,start+8)[0]
            length,address=struct.unpack_from('<II',data,start+16)
            return sha(data[address:address+min(vs,length)])
    raise ValueError('PE 실행 섹션 누락')
def bundle(setup):
    pos=setup.find(SIGNATURE)
    if pos<16:raise ValueError('Setup bundle header missing')
    offset,length=struct.unpack_from('<QQ',setup,pos-16)
    if not (offset>pos and length>0 and offset+length==len(setup)):raise ValueError('설치 데이터 범위 불일치 (서명 없는 내부 후보)')
    return setup[:offset],setup[offset:offset+length]
def collect(args):
    directory=args.directory.resolve();inputs=[f for f in directory.rglob('*') if f.is_file()]
    if any(f.parent not in {directory,directory/'stable'} for f in inputs):raise ValueError('공개 폴더의 예기치 않은 하위 폴더')
    def one(predicate,label):
        matched=[p for p in inputs if predicate(p)]
        if len(matched)!=1:raise ValueError(f'{label}: {len(matched)} found')
        return matched[0]
    src=one(lambda p:p.suffix.lower()=='.zip' and 'source' in p.name.lower(),'대응 소스')
    binary=one(lambda p:p.suffix.lower()=='.zip' and 'portable' in p.name.lower(),'Portable')
    exe=one(lambda p:p.name.lower().endswith('setup.exe'),'Setup')
    packed=one(lambda p:p.name.lower().endswith('-full.nupkg'),'업데이트 패키지') if not args.package else args.package.resolve()
    # Publish-directory exclusions apply to outer files in addition to inner payloads.
    for p in inputs:
        if p not in {src,binary,exe,packed} and p.name not in {'SHA256SUMS.txt','releases.stable.json','releases.stable.json.sig','RELEASES-stable','assets.stable.json'}:
            raise ValueError('공개 폴더 미등록 파일: '+p.name)
    portable=entries(binary.read_bytes(),binary.name)
    source=entries(src.read_bytes(),src.name)
    expected_version = ET.fromstring(source['directory.build.props'][1]).findtext('.//Version')
    if not expected_version or re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',expected_version) is None:
        raise ValueError('공개 소스에 올바른 릴리스 버전이 없습니다.')
    if binary.name != f'SonoHUD-v{expected_version}-win-x64-portable.zip' or src.name != f'SonoHUD-v{expected_version}-source.zip':
        raise ValueError('ZIP 파일명과 공개 소스의 버전이 다릅니다.')
    manifest=json.loads(source['source-manifest.json'][1])
    if {n:sha(b) for n,b in source.values() if n!='source-manifest.json'}!=manifest:raise ValueError('소스 manifest 불일치')
    if args.source_tree:
        staged={p.relative_to(args.source_tree).as_posix():sha(p.read_bytes()) for p in args.source_tree.rglob('*') if p.is_file()}
        if staged!={n:sha(b) for n,b in source.values()}:raise ValueError('공개 소스 스테이징 불일치')
    for required in PRODUCT_CODE:
        if required.lower() not in portable:raise ValueError('제품 어셈블리 누락: '+required)
    full=packed.read_bytes(); stub,payload=bundle(exe.read_bytes())
    if sha(payload)!=sha(full):raise ValueError('Setup 내장 패키지와 실제 업데이트 패키지가 다릅니다.')
    inspect_data(exe.name+' (부트스트랩)',stub)
    packaged=entries(full,packed.name)
    body={n[len('lib/app/'):].casefold():(n[len('lib/app/'):],b) for n,b in packaged.values() if n.lower().startswith('lib/app/')}
    if set(portable)-set(body):raise ValueError('업데이트에 없는 Portable 파일: '+repr(set(portable)-set(body)))
    different=[k for k,(n,b) in portable.items() if b!=body[k][1]]
    if different:raise ValueError('Portable/업데이트 제품 파일 불일치: '+repr(different))
    extra=set(body)-set(portable)
    additions={'squirrel.exe','sonohud_executionstub.exe','sq.version'}
    if body.get('sq.version',(None,None))[1]!=packaged['sonohud.core.nuspec'][1]:raise ValueError('설치된 앱 버전 메타데이터 불일치')
    if extra-additions:raise ValueError('예기치 않은 설치 추가 파일: '+repr(extra-additions))
    if not additions<=extra:raise ValueError('필수 설치 실행 도우미 누락')
    icon=source['src/sonohud/sonohud.app/assets/branding/sonohud.ico'][1]
    if packaged.get('setup.ico',(None,None))[1]!=icon:raise ValueError('설치 아이콘과 공개 소스 아이콘 불일치')
    for n,b in packaged.values():
        if n.lower().startswith('lib/app/'): continue
        if not (n.lower().endswith('.nuspec') or n=='setup.ico' or n=='[Content_Types].xml' or n=='_rels/.rels' or (n.startswith('package/services/metadata/core-properties/') and n.endswith('.psmdcp'))): raise ValueError('패키지 메타데이터 밖의 추가 파일: '+n)
    feed=one(lambda p:p.name=='releases.stable.json','stable 채널 목록')
    records=json.loads(feed.read_bytes())['Assets']
    if len(records)!=1:raise ValueError('예기치 않은 stable 채널 레코드')
    entry=records[0]
    if entry['FileName']!=packed.name or entry['PackageId']!='SonoHUD.Core' or entry['Version']!=expected_version or entry['Type']!='Full' or entry['Size']!=len(full) or entry['SHA256'].lower()!=sha(full):
        raise ValueError('stable 채널 레코드/파일 다름')
    for f in inputs:
        if f.suffix not in {'.nupkg','.zip','.exe'}:inspect_data(f.name,f.read_bytes())
    native={}
    for name,data in [('setup',stub),('update',body['squirrel.exe'][1]),('stub',body['sonohud_executionstub.exe'][1])]:
        ref=(args.vendor/(name+'_x64.exe')).read_bytes()
        if pe_code(ref)!=pe_code(data):raise ValueError(name+' 기계 코드가 수정 빌드 템플릿과 다릅니다.')
        native[name]={'templateSha256':sha(ref),'productTextSectionSha256':pe_code(data)}
    return {'binary':{'name':binary.name,'files':len(portable),'sha256':sha(binary.read_bytes())},'source':{'name':src.name,'files':len(source),'sha256':sha(src.read_bytes())},'setup':{'name':exe.name,'sha256':sha(exe.read_bytes()),'embeddedPackageSha256':sha(payload)},'update':{'name':packed.name,'entries':len(packaged),'sha256':sha(full)},'helperSections':native,'unexpectedEntries':[],'privateMarkers':[]}
def main(default_words=()):
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ['directory','vendor','result','source-tree','package']:
        parser.add_argument('--'+key,type=Path,required=key in ['directory','vendor','result'])
    parser.add_argument('--exclusions', type=Path, help='공개 소스 밖에 보관한 words 및 sha256 JSON')
    args=parser.parse_args()
    extra = json.loads(args.exclusions.read_text(encoding='utf-8-sig')) if args.exclusions else {}
    apply_exclusions([*default_words, *extra.get('words', [])], extra.get('sha256', []))
    result=collect(args)
    args.result.parent.mkdir(parents=True,exist_ok=True)
    args.result.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
    print(json.dumps(result,ensure_ascii=False))

if __name__ == '__main__':
    main()
