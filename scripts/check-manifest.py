"""Verify the exact upload inventory before promotion or signing."""
import argparse
import hashlib
from pathlib import Path
import re
import stat
import xml.etree.ElementTree as ET


def verify(directory, version, signed, add_signature):
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("잘못된 버전")
    expected = {f"SonoHUD-v{version}-source.zip", f"SonoHUD-v{version}-win-x64-portable.zip",
                "SonoHUD-Setup.exe", f"SonoHUD.Core-{version}-stable-full.nupkg", "releases.stable.json"}
    signature = "releases.stable.json.sig"
    actual = set()
    for entry in directory.iterdir():
        if not entry.is_file() or getattr(entry.lstat(), "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT or entry.is_symlink():
            raise ValueError(f"일반 파일이 아닙니다: {entry.name}")
        actual.add(entry.name)
    manifest = directory / "SHA256SUMS.txt"
    if actual != expected | {manifest.name} | ({signature} if signed or add_signature else set()):
        raise ValueError(f"업로드 대상 불일치: {sorted(actual)}")
    recorded = {}
    for line in manifest.read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"([a-f0-9]{64})  ([a-zA-Z0-9.\-]+)", line)
        if not match or match[2] in recorded:
            raise ValueError("중복되거나 잘못된 해시 항목")
        recorded[match[2]] = match[1]
    if set(recorded) != expected | ({signature} if signed else set()):
        raise ValueError("SHA256SUMS.txt 파일 목록 불일치")
    for name, digest in recorded.items():
        with (directory / name).open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                raise ValueError(f"SHA-256 불일치: {name}")
    if add_signature:
        data = (directory / signature).read_bytes()
        if len(data) != 64:
            raise ValueError("P-256 고정 길이 서명 오류")
        with manifest.open("a", encoding="utf-8") as stream:
            stream.write(f"{hashlib.sha256(data).hexdigest()}  {signature}\n")
        verify(directory, version, True, False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--signed", action="store_true")
    mode.add_argument("--add-verified-signature", action="store_true")
    args = parser.parse_args()
    version = ET.parse(Path(__file__).resolve().parents[1] / "Directory.Build.props").findtext(".//Version")
    verify(args.directory, version, args.signed, args.add_verified_signature)
