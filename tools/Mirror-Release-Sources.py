"""Reassemble reviewed source archives on GitHub's runner without building Citrus.

The manually supplied seed digest binds this job to the developer-machine audit.
Only pinned archives are downloaded. This never publishes the Citrus installer.
"""
import concurrent.futures
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPO = "TUAHASHAIKH/Citrus"
TAG = "citrus-sources-0.3.2"
LIMIT = 900 * 1024**2
PART = 768 * 1024**2


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        return None


def main():
    token = os.environ["GITHUB_TOKEN"]
    expected_seed = os.environ["CITRUS_SEED_SHA256"]
    if not re.fullmatch(r"[0-9a-f]{64}", expected_seed):
        raise ValueError("Expected reviewed seed digest")
    opener = urllib.request.build_opener(NoRedirect)
    def api(method, path):
        request = urllib.request.Request("https://api.github.com/repos/" + REPO + path, method=method,
            headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json", "User-Agent": "Citrus-source-mirror"})
        with opener.open(request, timeout=60) as response:
            body = response.read()
            return json.loads(body) if body else None
    matches = [item for item in api("GET", "/releases?per_page=100") if item["tag_name"] == TAG]
    if len(matches) != 1:
        raise ValueError("Expected exactly one reviewed source draft")
    release = matches[0]
    if not release["draft"]:
        raise ValueError("Source mirror only operates on the unpublished source draft")
    assets = {a["name"]: a for a in api("GET", "/releases/" + str(release["id"]) + "/assets?per_page=100")}
    folder = ROOT / "artifacts/source-release"
    stage = ROOT / "artifacts/source-mirror"
    folder.mkdir(parents=True, exist_ok=True)
    stage.mkdir(parents=True, exist_ok=True)
    for name in ("SOURCE_MANIFEST.json", "BUILDING-SOURCES.txt", "Join-FFmpeg-Source-Cache.ps1", "Citrus-0.3.2-source.tar.gz", "Microsoft-VC14-license.docx"):
        asset = assets[name]
        request = urllib.request.Request("https://api.github.com/repos/" + REPO + "/releases/assets/" + str(asset["id"]) + "?nonce=" + str(time.time_ns()),
            headers={"Authorization": "Bearer " + token, "Accept": "application/octet-stream", "User-Agent": "Citrus-source-mirror"})
        try:
            response = opener.open(request, timeout=60)
        except urllib.error.HTTPError as error:
            if error.code != 302:
                raise RuntimeError("Seed asset download failed: HTTP " + str(error.code)) from None
            url = error.headers["Location"]
            parsed = urllib.parse.urlparse(url)
            if parsed.scheme != "https" or not parsed.hostname.endswith(".githubusercontent.com"):
                raise ValueError("Unexpected seed storage host")
            response = urllib.request.urlopen(url, timeout=60)  # No credentials forwarded.
        with response, (folder / name).open("wb") as output:
            shutil.copyfileobj(response, output)
    seed_path = folder / "SOURCE_MANIFEST.json"
    if digest(seed_path) != expected_seed:
        raise ValueError("Source manifest differs from the reviewed audit")
    manifest = json.loads(seed_path.read_text())
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    if manifest["version"] != "0.3.2" or manifest["ffmpeg_build"] != lock:
        raise ValueError("Seed does not match this source release")
    seed_copy = stage / "SOURCE_MANIFEST-SEED.json"
    shutil.copy2(seed_path, seed_copy)
    files = [(seed_copy, "provenance/SOURCE_MANIFEST-SEED.json")]
    def download(url, path, expected):
        if urllib.parse.urlparse(url).scheme != "https" or not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise ValueError("Unpinned source input")
        path.parent.mkdir(parents=True, exist_ok=True)
        for attempt in range(4):
            try:
                request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 Citrus-source-mirror"})
                with urllib.request.urlopen(request, timeout=120) as source, path.open("wb") as output:
                    shutil.copyfileobj(source, output)
                if digest(path) != expected:
                    raise ValueError("Source digest mismatch: " + path.name)
                return path
            except (OSError, urllib.error.URLError):
                if attempt == 3:
                    raise
                time.sleep(2)
    def collect(item):
        relative = item["packaged_file"]
        target = (stage / relative).resolve()
        if not target.is_relative_to(stage.resolve()):
            raise ValueError("Unsafe seed path")
        if item["component"] == "Citrus source and build scripts":
            commit = item["git_commit"]
            if not re.fullmatch(r"[0-9a-f]{40}", commit):
                raise ValueError("Invalid source commit")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(folder / "Citrus-0.3.2-source.tar.gz", target)
            if digest(target) != item["sha256"]:
                raise ValueError("Exported Citrus source differs from reviewed archive")
        else:
            download(item["upstream_url"], target, item["sha256"])
        print("Verified " + item["component"], flush=True)
        return target, relative
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        files.extend(pool.map(collect, manifest["archives"]))
    runtime_terms = stage / "Microsoft-VC14-license.docx"
    shutil.copy2(folder / "Microsoft-VC14-license.docx", runtime_terms)
    if digest(runtime_terms) != "08651651a7602fc7c0e2763de0fde1ff9f868df2780597cd1775ee9d6441c783":
        raise ValueError("Runtime terms seed differs from the publisher document")
    files.append((runtime_terms, "build-inputs/Microsoft-VC14-license.docx"))
    cache = ROOT / ".engines/compliance"
    cache.mkdir(parents=True, exist_ok=True)
    dependency = cache / lock["dependency_sources"]["file"]
    subprocess.run(["python3", str(ROOT / "tools/Fetch-GitHub-Artifact.py"), lock["publisher"],
        str(lock["dependency_sources"]["artifact"]), str(dependency), "--sha256", lock["dependency_sources"]["sha256"]], check=True)
    with dependency.open("rb") as source:
        for part in manifest["ffmpeg_dependency_cache"]["parts"]:
            if not re.fullmatch(r"ffmpeg-dependency-cache\.zip\.\d{3}", part["file"]):
                raise ValueError("Unsafe source part name")
            target = folder / part["file"]
            with target.open("wb") as output:
                remaining = part["size"]
                if remaining <= 0 or remaining > PART:
                    raise ValueError("Unexpected source part length")
                while remaining:
                    chunk = source.read(min(1024**2, remaining))
                    if not chunk:
                        raise ValueError("Truncated source cache")
                    output.write(chunk)
                    remaining -= len(chunk)
            if digest(target) != part["sha256"]:
                raise ValueError("Source part differs from the reviewed cache")
        if source.read(1):
            raise ValueError("Unexpected extra source cache bytes")
    # Reuse the full notice/credential scan before making the cache public.
    spec = importlib.util.spec_from_file_location("notices", ROOT / "tools/Collect-Release-Notices.py")
    notices = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(notices)
    import sys
    sys.argv = ["Collect-Release-Notices.py", "--payload", str(stage / "audit"), "--sources", str(stage / "audit-sources")]
    audit_sources = stage / "audit-sources"
    audit_sources.mkdir(exist_ok=True)
    (audit_sources / "source-review.json").write_text(json.dumps({"notices": [], "native_source_archives": []}))
    notices.main()
    bundle_records = []
    archive = None
    try:
        size = 0
        for path, relative in files:
            if path.stat().st_size > LIMIT:
                raise ValueError("Unexpected oversized archive")
            if archive is None or size + path.stat().st_size > LIMIT:
                if archive:
                    archive.close()
                name = "Citrus-0.3.2-Sources-" + str(len(bundle_records) + 1).zfill(2) + ".zip"
                archive = zipfile.ZipFile(folder / name, "w", compression=zipfile.ZIP_STORED)
                bundle_records.append(dict(file=name))
                size = 0
            archive.write(path, relative)
            size += path.stat().st_size
    finally:
        if archive:
            archive.close()
    for item in bundle_records:
        path = folder / item["file"]
        with zipfile.ZipFile(path) as bundle:
            if bundle.testzip() is not None:
                raise ValueError("Source bundle CRC failure")
        item.update(size=path.stat().st_size, sha256=digest(path))
    manifest["source_bundles"] = bundle_records
    manifest["assembly"] = dict(reviewed_seed_sha256=expected_seed, workflow_commit=os.environ.get("GITHUB_SHA"))
    seed_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    binary = cache / lock["binary"]["file"]
    download("https://github.com/" + lock["publisher"] + "/releases/download/" + lock["release"] + "/" + binary.name,
             binary, lock["binary"]["sha256"])
    assets_to_hash = [folder / item["file"] for item in bundle_records + manifest["ffmpeg_dependency_cache"]["parts"]]
    assets_to_hash += [folder / name for name in ("SOURCE_MANIFEST.json", "BUILDING-SOURCES.txt", "Join-FFmpeg-Source-Cache.ps1")]
    assets_to_hash.append(binary)
    (folder / "SHA256SUMS.txt").write_text("".join(digest(p) + "  " + p.name + "\n" for p in assets_to_hash), encoding="ascii")
    # Only remove the exact reviewed seed from this draft; the final publisher
    # refuses to overwrite any uploaded source asset with different contents.
    if assets["SOURCE_MANIFEST.json"].get("digest") != "sha256:" + expected_seed:
        raise ValueError("Seed asset changed during assembly")
    api("DELETE", "/releases/assets/" + str(assets["SOURCE_MANIFEST.json"]["id"]))
    own_source = next(item for item in manifest["archives"] if item["component"] == "Citrus source and build scripts")
    if assets["Citrus-0.3.2-source.tar.gz"].get("digest") != "sha256:" + own_source["sha256"]:
        raise ValueError("Citrus source seed asset changed")
    api("DELETE", "/releases/assets/" + str(assets["Citrus-0.3.2-source.tar.gz"]["id"]))
    if assets["Microsoft-VC14-license.docx"].get("digest") != "sha256:" + digest(runtime_terms):
        raise ValueError("Runtime license seed asset changed")
    api("DELETE", "/releases/assets/" + str(assets["Microsoft-VC14-license.docx"]["id"]))
    subprocess.run(["python3", str(ROOT / "tools/Publish-Release-Sources.py"), "--publish"], check=True)


if __name__ == "__main__":
    main()
