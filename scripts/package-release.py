"""Build an internal x64 release pair from one allowlisted source snapshot (Python 3.10+)."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tarfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
import importlib.util
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
APP = Path("src/SonoHUD/SonoHUD.App")
CORE = Path("src/SonoHUD/SonoHUD.Core")
CONTRACTS = Path("src/SonoHUD/SonoHUD.PluginContracts")
DOCS = {
    "docs/release/core-user-guide.md": "사용안내.md",
    "docs/release/M7.2-core-privacy.md": "개인정보와-보안.md",
}
YAML_COMMIT = "748334a8fa7c227740018b284b71ad95cc6b7fc7"
RUNTIME_COMMIT = "4271d88e0aebf3d04f188f1334c2220d80555ef6"
HOST_VMR = "95017c711e6afc1085133d440e42b4bd78155701"
YAML_SHA = "591149cd4d25b4e378fafbbc656e297b06e927a68f68310fb717ca5e2d61bb66"
RUNTIME_SHA = "0b8dbfa926187414b8a8fe52c12a6d34d44b78e130c11348916d9675199e4cc5"
LICENSE_DIRS = ("CsWinRT-2.2.0", "Windows-Metadata-MIT", "DotNet-AppHost-10.0.12", "FluentFlyout-v2.16.1", "Velopack-1.2.161")
PACKAGE_ROOT = {"LICENSE","NOTICE","THIRD-PARTY-NOTICES.md","SonoHUD.exe","SonoHUD.dll","SonoHUD.Core.dll",
                "YamlDotNet.dll","Velopack.dll","SonoHUD.PluginContracts.dll","SonoHUD.deps.json","SonoHUD.runtimeconfig.json",*DOCS.values()}
FONT_NAMES = ("WantedSans","Pretendard","PretendardJP","JetBrainsMono","Inter")


def digest(file):
    with file.open("rb") as stream:
        sha = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024*1024), b""):
            sha.update(chunk)
        return sha.hexdigest()


def manifest(folder):
    return {p.relative_to(folder).as_posix(): digest(p) for p in sorted(folder.rglob("*")) if p.is_file()}


def registered_licenses():
    result = {"licenses/YamlDotNet-18.1.0.nuspec", "licenses/YamlDotNet-18.1.0-MIT.txt", "licenses/YamlDotNet-18.1.0-libyaml.txt"}
    for dirname in LICENSE_DIRS:
        root = ROOT/"licenses"/dirname
        for p in root.rglob("*"):
            if dirname == "FluentFlyout-v2.16.1" and p == root/"LICENSE":
                continue
            if p.is_file() and (p.name.startswith("LICENSE") or p.suffix.lower() in {".md",".txt",".json",".nuspec",".cs",".xaml"}):
                result.add(p.relative_to(ROOT).as_posix())
    result.add("licenses/Velopack-1.2.161/RUST-STDLIB-COPYRIGHT.html")
    if "licenses/FluentFlyout-v2.16.1/UPSTREAM-ATTRIBUTION.md" not in result:
        raise FileNotFoundError("FluentFlyout 저작자 및 GPL 원문 고지가 필요합니다.")
    return result


def copy_snapshot(stage):
    files = {"README.md","Directory.Build.props","global.json","BUILD-EMBEDDED.md","LICENSE","NOTICE","THIRD-PARTY-NOTICES.md",
             "scripts/Build-Portable.ps1","scripts/New-ReleaseCandidate.ps1","scripts/package-release.py",
             "scripts/Prepare-Velopack-Source.py","scripts/velopack-windows.lock","scripts/velopack-cleanup.c","scripts/velopack-uninstall.rs","scripts/velopack-legacy-owned.tsv","scripts/Build-Velopack-Windows.ps1","scripts/New-CoreSetup.ps1",
             "docs/architecture/M7.1-plugin-sdk.md","docs/architecture/M7.1-plugin-architecture.md",
             "third_party/embedded-winrt-provenance.json", *DOCS, *registered_licenses(),
             ".github/workflows/release-candidate.yml", "CHANGELOG.md",
             "docs/release/known-limitations.md","docs/release/release-notes.md","docs/release/release-pipeline.md",
             "scripts/audit-release.py","scripts/New-Distribution.ps1","scripts/Set-ReleaseFeed.ps1",
             ".gitignore",".gitattributes","README.en.md","SECURITY.md","docs/screenshots/core-home.png","docs/screenshots/core-overlay.png",
             "docs/release/update-trust.json","docs/release/M8.1-v1.0.0-release-notes.md","docs/release/M8.1-signing-key-policy.md",
             "docs/release/M8.1-github-release-playbook.md","scripts/Manage-ReleaseKey.ps1","scripts/check-release-slot.py",
             "scripts/Sign-ReleaseFeed.ps1","scripts/check-manifest.py","scripts/Install-NativeToolchain.py"}
    # Only the license-safe, deterministic core suite travels in the public repository.
    suite=ROOT/"src/SonoHUD/SonoHUD.Tests"
    for name in ("SonoHUD.Tests.csproj","Program.cs","SpectrumTests.cs","DesktopLayoutTests.cs","TaskbarConfigTests.cs",
                 "OverlayTests.cs","PresetDesignTests.cs","OverlayResizeGeometryTests.cs","packages.lock.json"):
        files.add((suite/name).relative_to(ROOT).as_posix())
    # Include SDK inputs and product source only. Historical probes, logs and bin/obj are outside this snapshot.
    for folder in [APP, CORE, CONTRACTS]:
        for p in (ROOT/folder).rglob("*"):
            rel = p.relative_to(ROOT)
            if not p.is_file() or {"bin","obj","Assets"}.intersection(rel.parts):
                continue
            if p.suffix == ".csproj" and p != ROOT/folder/(folder.name+".csproj"):
                continue  # WPF can create temporary projects alongside the real project during a build.
            if p.suffix in {".cs",".xaml",".csproj",".targets",".props",".resx",".manifest"} or p.name=="packages.lock.json":
                files.add(rel.as_posix())
    assets=APP/"Assets"
    font_manifest=json.loads((ROOT/assets/"Fonts/font-manifest.json").read_text(encoding="utf-8-sig"))
    families=font_manifest["families"] if "families" in font_manifest else font_manifest["Families"]
    for family in families:
        for font in family.get("Files", family.get("files", [])):
            rel=assets/"Fonts"/font["Path"]
            if digest(ROOT/rel).upper()!=font["SHA256"].upper():
                raise ValueError(f"글꼴 SHA-256 불일치: {rel}")
            files.add(rel.as_posix())
    for rel in ["Fonts/font-manifest.json","Fonts/README.md",*(f"Fonts/{f}/OFL.txt" for f in FONT_NAMES),
                "Branding/C-wave-refinement-v2.png","Branding/SonoHUD.ico","Branding/branding-manifest.json",
                "Branding/Generate-Icons.ps1","Branding/README.md"]:
        files.add((assets/rel).as_posix())
    for f in ["LICENSE","Microsoft.Windows.CsWinRT.nuspec"]:
        files.add("third_party/CsWinRT-2.2.0/"+f)
    provenance=json.loads((ROOT/"third_party/embedded-winrt-provenance.json").read_text(encoding="utf-8-sig"))
    for entry in provenance["embeddedRuntime"]:
        path=entry["path"]
        if digest(ROOT/path)!=entry["sha256"].lower():
            raise ValueError(f"고정된 CsWinRT 소스 불일치: {path}")
        files.add(path)
    for f in ["Windows.winmd","LICENSE-MIT","LICENSE-APACHE","UPSTREAM-README.md"]:
        files.add("third_party/windows-metadata/"+f)
    for rel in sorted(files):
        src,dst=ROOT/rel,stage/rel
        if not src.is_file():
            raise FileNotFoundError(src)
        dst.parent.mkdir(parents=True,exist_ok=True)
        if src.is_symlink() or any(p.is_symlink() for p in src.parents):
            raise ValueError(f"소스 경로에 링크가 있습니다: {rel}")
        shutil.copyfile(src,dst)
    solution = ET.Element("Solution")
    for folder in [APP,CORE,CONTRACTS]:
        ET.SubElement(solution,"Project",Path=(folder/(folder.name+".csproj")).as_posix())
    ET.indent(solution,space="  ")
    ET.ElementTree(solution).write(stage/"SonoHUD.slnx",encoding="utf-8",xml_declaration=True)


def upstream(cache,name,url,sha):
    cache.mkdir(parents=True,exist_ok=True)
    final=cache/name
    if not final.exists():
        part=final.with_suffix(final.suffix+".part")
        with urllib.request.urlopen(url,timeout=100) as response, part.open("wb") as out:
            shutil.copyfileobj(response,out)
        if digest(part)!=sha:
            raise ValueError(f"공급 소스 해시가 다릅니다: {name}")
        part.replace(final)
    if digest(final)!=sha:
        raise ValueError(f"캐시 해시가 다릅니다: {name}")
    return final


def archive_member(name):
    parts=PurePosixPath(name).parts[1:]  # pinned upstream archive has one root
    if not parts or any(p in {"..","."} or ":" in p or "\\" in p for p in parts):
        raise ValueError(name)
    return "/".join(parts)


def write_entry(dst,data):
    dst.parent.mkdir(parents=True,exist_ok=True)
    dst.write_bytes(data)


def dependencies(stage,cache):
    folder=stage/"vendor-source"
    spec=importlib.util.spec_from_file_location("velopack_source",ROOT/"scripts/Prepare-Velopack-Source.py")
    vendoring=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(vendoring)
    vendoring.source_tree(cache,folder/"Velopack")
    # Repack only the unmodified source and build inputs needed for the shipped library.
    # In particular, the upstream signing private key and its test corpus never enter either ZIP.
    yaml_uri=f"https://codeload.github.com/aaubry/YamlDotNet/zip/{YAML_COMMIT}"
    with zipfile.ZipFile(upstream(cache,"yaml-upstream.zip",yaml_uri,YAML_SHA)) as z:
        for info in z.infolist():
            if info.is_dir():
                continue
            rel=archive_member(info.filename)
            if rel.startswith("YamlDotNet/") or rel in {"Directory.Build.props","Directory.Packages.props",".editorconfig",
                    "LICENSE-libyaml","LICENSE.txt","README.md","GitVersion.yml","yamldotnet.png"}:
                write_entry(folder/"YamlDotNet"/rel,z.read(info))
    runtime_uri=f"https://codeload.github.com/dotnet/runtime/tar.gz/{RUNTIME_COMMIT}"
    archive=upstream(cache,"runtime-upstream.tar.gz",runtime_uri,RUNTIME_SHA)
    # The packaged .NET apphost template is an SDK component. Keep editable native host
    # sources and build inputs here, and identify Microsoft's complete matching build trees.
    with tarfile.open(archive,"r|gz") as t:
        for member in t:
            if not member.isfile():
                continue
            rel=archive_member(member.name)
            directories=("src/native/corehost/","src/native/minipal/","src/native/inc/","src/native/public/",
                         "src/native/containers/","eng/native/")
            if {"tests","test","testassets"}.intersection(PurePosixPath(rel).parts):
                continue
            if rel.startswith(directories) or rel in {"CMakeLists.txt","src/native/CMakeLists.txt","LICENSE.TXT",
                    "THIRD-PARTY-NOTICES.TXT","README.md","build.cmd","build.sh","global.json"}:
                write_entry(folder/"dotnet-runtime"/rel,t.extractfile(member).read())
    readme=f"""# 배포 라이브러리의 소스
- **YamlDotNet 18.1.0**: https://github.com/aaubry/YamlDotNet/tree/{YAML_COMMIT} — 공급 NuGet의 repository commit. YamlDotNet/에 수정 가능한 라이브러리 소스, 공통 프로젝트 설정 및 두 라이선스를 포함했다. 공식 패키지를 변경 없이 복원하는 앱 빌드는 scripts/Build-Portable.ps1을 사용한다. 라이브러리를 직접 수정하여 빌드할 때는 YamlDotNet 폴더에서 `dotnet build YamlDotNet/YamlDotNet.csproj -c Release -f net10.0 -p:SignAssembly=false`를 사용할 수 있다. 공급자 서명 개인키는 배포하지 않는다. 자체 수정 빌드의 서명/바이트 일치는 기대하지 않는다.
- **.NET apphost 10.0.12**: .NET SDK 10.0.401이 x64 실행 템플릿을 제공한다. 이 템플릿과 SonoHUD 리소스로 SonoHUD.exe를 생성한다. dotnet-runtime/에 해당 버전의 네이티브 호스트, 공용 헤더와 네이티브 빌드 스크립트를 제공한다. 전체 Microsoft 빌드 트리와 모든 도구는 https://github.com/dotnet/dotnet/tree/{HOST_VMR} 및 https://github.com/dotnet/runtime/tree/{RUNTIME_COMMIT} 에 있다. 두 커밋의 연결은 https://github.com/dotnet/dotnet/blob/{HOST_VMR}/src/source-manifest.json 에서 확인할 수 있다. Windows Desktop Runtime은 사용자 설치 구성 요소다.
- **Velopack**: 1.2.161의 .NET 클라이언트 및 Windows 설치기 편집 소스는 Velopack/에 있습니다. scripts/Build-Velopack-Windows.ps1과 BUILD-EMBEDDED.md를 참조하십시오.
- **Embedded WinRT**: 별도 허용 라이선스 Windows.winmd 및 CsWinRT 런타임 소스는 third_party/, 해시·재생성 프로젝트 설정은 BUILD-EMBEDDED.md에 있다.
"""
    (folder/"README.md").write_text(readme,encoding="utf-8")


def archive_tree(folder,outfile):
    # Sorted names, fixed ZIP headers and identical compression remove extraction time as an input.
    with zipfile.ZipFile(outfile,"x",compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for file in sorted(p for p in folder.rglob("*") if p.is_file()):
            entry=zipfile.ZipInfo(file.relative_to(folder).as_posix(),(1980,1,1,0,0,0))
            entry.create_system=3
            entry.external_attr=0o100644<<16
            z.writestr(entry,file.read_bytes(),compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)


def empty_directory(path):
    if path.exists() and (not path.is_dir() or any(path.iterdir())):
        raise FileExistsError(f"기존 자료를 보존하려면 빈 폴더를 지정하세요: {path}")
    path.mkdir(parents=True,exist_ok=True)


def write_json(path,data):
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")


def build(source,target,log):
    powershell=shutil.which("pwsh.exe") or shutil.which("powershell.exe")
    if not powershell:
        raise RuntimeError("Windows PowerShell이 필요합니다.")
    with log.open("wb") as stream:
        subprocess.run([powershell,"-NoProfile","-NonInteractive","-File",str(source/"scripts/Build-Portable.ps1"),
                        "-OutputPath",str(target)],cwd=source,stdout=stream,stderr=subprocess.STDOUT,check=True)


def check_publish(files):
    names=set(files)
    allowed=PACKAGE_ROOT|registered_licenses()|{"Assets/Fonts/README.md","Assets/Fonts/font-manifest.json",
                                              *(f"Assets/Fonts/{f}/OFL.txt" for f in FONT_NAMES)}
    if names!=allowed:
        raise ValueError(f"배포 파일 목록 불일치. 누락: {sorted(allowed-names)} / 추가: {sorted(names-allowed)}")


def run(args):
    work,out,evidence=map(Path.resolve,(args.work,args.output,args.evidence))
    if len({work,out,evidence})!=3 or any(a in b.parents for a in (work,out,evidence) for b in (work,out,evidence)):
        raise ValueError("작업, 증거, 산출물은 서로 독립된 폴더여야 합니다.")
    for d in (work,out,evidence):
        empty_directory(d)
    stage=work/"source-snapshot"
    stage.mkdir()
    copy_snapshot(stage)
    dependencies(stage,(args.upstream_cache or work/"upstream-cache").resolve())
    version=ET.parse(stage/"Directory.Build.props").findtext(".//Version")
    if not version or not all(p.isdigit() for p in version.split(".")):
        raise ValueError("제품 버전 형식이 올바르지 않습니다.")
    base="SonoHUD-v"+version
    srczip=work/(base+"-source.zip")
    binzip=work/(base+"-win-x64-portable.zip")
    sources=manifest(stage)
    write_json(stage/"source-manifest.json",sources)
    archive_tree(stage,srczip)
    publications=[]
    generated=[]
    for index in [1,2]:
        # Different absolute paths, including spaces, reveal accidental machine-specific build inputs.
        checkout=work/("source-A" if index==1 else "independent B")
        with zipfile.ZipFile(srczip) as z:
            z.extractall(checkout)
        if manifest(checkout)!=manifest(stage):
            raise ValueError("독립 소스 압축 해제 불일치")
        dst=work/f"publish-{index}"
        build(checkout,dst,evidence/f"publish-{index}.log")
        publications.append(manifest(dst))
        derived=checkout/APP/"obj/Release/net10.0-windows10.0.26100.0/win-x64/Generated Files/CsWinRT"
        if not (derived/"WinRT.cs").exists() and not list(derived.glob("*.cs")):
            raise ValueError("WinRT 투영 생성 결과를 찾을 수 없습니다.")
        generated.append({p.name:digest(p) for p in sorted(derived.glob("*.cs"))})
    a,b=publications
    check_publish(a);check_publish(b)
    diff=sorted(k for k in a.keys()|b.keys() if a.get(k)!=b.get(k))
    write_json(evidence/"independent-build.json",{
        "version":version,"sdk":json.loads((stage/"global.json").read_text(encoding="utf-8-sig"))["sdk"]["version"],
        "sourceSha256":digest(srczip),"sourceFileCount":len(sources)+1,"binaryFileCount":len(a),
        "publish1":a,"publish2":b,"differentFiles":diff,"generatedCount":len(generated[0]),
        "generatedDifferent":sorted(k for k in generated[0].keys()|generated[1].keys() if generated[0].get(k)!=generated[1].get(k))})
    if diff or generated[0]!=generated[1]:
        raise ValueError("독립 빌드가 다릅니다. independent-build.json과 두 publish 폴더를 비교하세요.")
    archive_tree(work/"publish-1",binzip)
    with zipfile.ZipFile(binzip) as z:
        packaged={name:hashlib.sha256(z.read(name)).hexdigest() for name in z.namelist()}
    if packaged!=a:
        raise ValueError("실행 ZIP과 publish-1 내용이 다릅니다.")
    write_json(evidence/"binary-files.json",[{"path":k,"bytes":(work/"publish-1"/k).stat().st_size,"sha256":v} for k,v in a.items()])
    pair={}
    for item in (srczip,binzip):
        sha=digest(item)
        shutil.copyfile(item,out/item.name)
        if digest(out/item.name)!=sha:raise IOError("최종 복사 해시 불일치")
        pair[item.name]={"bytes":item.stat().st_size,"sha256":sha}
    write_json(evidence/"release-pair.json",pair)
    print(json.dumps(pair,indent=2))


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ("work","output","evidence"):
        parser.add_argument("--"+name,type=Path,required=True)
    parser.add_argument("--upstream-cache",type=Path)
    run(parser.parse_args())
