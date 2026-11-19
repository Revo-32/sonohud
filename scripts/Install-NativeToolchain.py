"""Install the pinned open-source Windows build tools in a new local directory."""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import shutil
import tarfile
import urllib.request
import zipfile

RUST_TARGET = "x86_64-pc-windows-gnullvm"
RUST_VERSION = "1.100.0"
LLVM_NAME = "llvm-mingw-20261117-ucrt-x86_64"
# SHA-256 from Rust's dated release manifest and the llvm-mingw release asset.
RUST_HASHES = {'rustc': '1d4f93b7264c098bf90c86b48b7953a9009f7dc01e4c2bdc8ba30d9407ed47d9', 'rust-std': '5e63b95c73bd783864b4350792c27292d67aab8e3033bf49381d0e55ef4384cf', 'cargo': '626445dd78b4f8bf2a06862a47b8ec26ba211de52fd4c69f4c770536f2f6b9cc', 'rust-mingw': '6a280d6a1800583689190fc50caf4bc713f716879c9e810311bf9a6a7834f6c8'}
LLVM_HASH = "4d002e330a0f703625aa5ccbc37e96f8f824413e163bfc7336774aabd2c3a412"

def download(url, digest, folder):
    dest = folder / url.rsplit("/", 1)[1]
    with urllib.request.urlopen(url, timeout=180) as response, dest.open("xb") as output:
        shutil.copyfileobj(response, output, length=1 << 20)
    with dest.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
            raise ValueError(f"SHA-256 mismatch for {dest.name}")
    return dest

def parts(name):
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or ":" in name or chr(92) in name:
        raise ValueError(f"Unexpected toolchain archive path: {name}")
    return path.parts

def main(root):
    root.mkdir(parents=True, exist_ok=False)
    for package, digest in RUST_HASHES.items():
        name = f"{package}-{RUST_VERSION}-{RUST_TARGET}"
        archive = download(f"https://static.rust-lang.org/dist/2026-11-12/{name}.tar.xz", digest, root)
        with tarfile.open(archive, "r|xz") as data:
            for item in data:
                path = parts(item.name)
                if len(path) < 4 or path[0] != name or not item.isfile():
                    continue
                if path[2] not in {"bin", "lib", "libexec", "etc", "share"}:
                    continue
                target = root / "rust" / Path(*path[2:])
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open("wb") as output:
                    shutil.copyfileobj(data.extractfile(item), output)
    archive = download(f"https://github.com/mstorsjo/llvm-mingw/releases/download/20261117/{LLVM_NAME}.zip", LLVM_HASH, root)
    with zipfile.ZipFile(archive) as data:
        for item in data.infolist():
            path = parts(item.filename)
            if not path or path[0] != LLVM_NAME:
                raise ValueError("Unexpected compiler archive prefix")
            if item.is_dir():
                continue
            target = root / Path(*path)
            target.parent.mkdir(parents=True, exist_ok=True)
            with data.open(item) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)

if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("output", type=Path, help="new directory on the build machine")
    main(cli.parse_args().output.resolve())
