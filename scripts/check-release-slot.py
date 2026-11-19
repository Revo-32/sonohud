"""Read-only preflight for the first GitHub release; never publishes or uploads an asset."""
import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET


def check_existing(tag, tag_exists, releases):
    if tag_exists or any(item.get("tag_name") == tag for item in releases):
        raise ValueError(f"{tag} 태그 또는 공개·초안 릴리스가 이미 있습니다. 자동 덮어쓰기를 중단합니다.")


def get(path, allow_missing=False):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "SonoHUD Release Engineering"}
    if os.environ.get("GH_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["GH_TOKEN"]
    request = urllib.request.Request("https://api.github.com" + path, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if allow_missing and error.code == 404:
            return None
        raise


def main():
    root = Path(__file__).resolve().parents[1]
    version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
    trust = json.loads((root / "docs/release/update-trust.json").read_text(encoding="utf-8-sig"))
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("정식 버전 형식 오류")
    slug = trust["owner"] + "/" + trust["repository"]
    if slug != "Revo-32/sonohud" or os.environ.get("GITHUB_REPOSITORY", slug) != slug:
        raise ValueError("승인된 저장소가 아닙니다.")
    base = "/repos/" + slug
    repo = get(base)
    if repo.get("full_name") != slug or repo.get("archived") or repo.get("disabled"):
        raise ValueError("저장소가 준비되지 않았습니다.")
    tag = "v" + version
    tag_exists = get(base + "/git/ref/tags/" + tag, allow_missing=True) is not None
    page = 1
    while True:
        releases = get(base + f"/releases?per_page=100&page={page}")
        check_existing(tag, tag_exists, releases)
        if len(releases) < 100:
            break
        page += 1


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError) as error:
        sys.exit(f"배포 사전 확인 실패: {error}")
