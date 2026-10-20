"""Collect upstream source archives and notices; this does not certify compliance.

Run with Python 3.13+: python tools/Collect-Release-Sources.py
Archives are never executed or extracted. Downloads are cached and re-hashed.
"""
import concurrent.futures
import hashlib
import importlib.metadata
import json
from pathlib import Path
import shutil
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
PAYLOAD = ROOT / "artifacts/payload"
OUTPUT = ROOT / "artifacts/source-review"


def read_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Citrus-source-review/0.3"})
    with urllib.request.urlopen(request, timeout=90) as response:
        return json.load(response)


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(url, destination, expected=None):
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not destination.exists() or (expected and sha256(destination) != expected):
        temporary = destination.with_name(destination.name + ".partial")
        request = urllib.request.Request(url, headers={"User-Agent": "Citrus-source-review/0.3"})
        try:
            with urllib.request.urlopen(request, timeout=45) as response, temporary.open("wb") as stream:
                shutil.copyfileobj(response, stream)
            if expected and sha256(temporary) != expected:
                raise ValueError("Source archive digest mismatch: " + destination.name)
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    return {"file": destination.relative_to(OUTPUT).as_posix(), "url": url, "sha256": sha256(destination)}


def package_source(item):
    name, version = item
    result = {"name": name, "version": version}
    try:
        metadata = read_json("https://pypi.org/pypi/{}/{}/json".format(urllib.parse.quote(name), urllib.parse.quote(version)))
        sources = [file for file in metadata["urls"] if file["packagetype"] == "sdist"]
        if len(sources) != 1:
            raise ValueError("Expected one source distribution, found " + str(len(sources)))
        source = sources[0]
        if urllib.parse.urlparse(source["url"]).hostname != "files.pythonhosted.org":
            raise ValueError("Unexpected PyPI source host")
        filename = Path(source["filename"])
        if filename.name != source["filename"]:
            raise ValueError("Invalid source archive filename")
        result.update(download(source["url"], OUTPUT / "python-packages" / filename, source["digests"]["sha256"]))
        result["status"] = "archive-collected; native dependencies and build materials require review"
    except Exception as error:
        result.update(status="blocked", error=str(error))
    print(name + " " + version + ": " + result["status"], flush=True)
    return result


def main():
    runtime = json.loads((PAYLOAD / "engine/runtime.json").read_text(encoding="utf-8-sig"))
    libraries = PAYLOAD / "engine" / runtime["Libraries"]
    distributions = list(importlib.metadata.distributions(path=[str(libraries)]))
    if not distributions:
        raise RuntimeError("Build the self-contained payload first.")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    notices = []
    for distribution in distributions:
        name, version = distribution.metadata["Name"], distribution.version
        for relative in distribution.files or []:
            if not any(word in str(relative).lower() for word in ["license", "copying", "notice"]):
                continue
            source = Path(distribution.locate_file(relative)).resolve()
            if not source.is_relative_to(libraries.resolve()) or not source.is_file():
                raise ValueError("Notice path is outside the bundled libraries: " + str(relative))
            target = OUTPUT / "notices" / name / source.relative_to(libraries.resolve())
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            notices.append({"package": name, "version": version, "file": target.relative_to(OUTPUT).as_posix(), "sha256": sha256(target)})
    items = sorted((distribution.metadata["Name"], distribution.version) for distribution in distributions)
    versions = {name.lower().replace("-", "_"): version for name, version in items}
    if versions.get("pillow_heif") != "1.8.1" or versions.get("imageio_ffmpeg") != "0.6.0":
        raise ValueError("Native source mappings are for pillow-heif 1.8.1 and imageio-ffmpeg 0.6.0; review mappings before collecting a different build.")
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        packages = list(pool.map(package_source, items))
    python_exe = PAYLOAD / "engine" / runtime["Python"]
    import subprocess
    python_version = subprocess.check_output([str(python_exe), "--version"], text=True).strip().split()[1]
    upstream = []
    sources = [
        ("CPython", "https://www.python.org/ftp/python/{0}/Python-{0}.tar.xz".format(python_version), "Python-" + python_version + ".tar.xz"),
        ("FFmpeg base source (not the complete Gyan build)", "https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/refs/tags/n7.1", "ffmpeg-7.1.tar.gz"),
        ("libheif", "https://codeload.github.com/strukturag/libheif/tar.gz/refs/tags/v1.23.6", "libheif-1.23.6.tar.gz"),
        ("libde265", "https://codeload.github.com/strukturag/libde265/tar.gz/refs/tags/v1.1.3", "libde265-1.1.3.tar.gz"),
        ("x265", "https://codeload.github.com/Multicorewareinc/x265/tar.gz/refs/tags/4.3", "x265-4.3.tar.gz"),
    ]
    def native_source(item):
        name, url, filename = item
        try:
            result = dict(name=name, **download(url, OUTPUT / "native-sources" / filename))
            print(name + ": archive collected", flush=True)
        except Exception as error:
            result = dict(name=name, status="blocked", error=str(error), url=url)
            print(name + ": " + str(error), flush=True)
        return result
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        upstream = list(pool.map(native_source, sources))
    report = {
        "public_installer_ready": False,
        "packages": packages, "notices": notices, "native_source_archives": upstream,
        "remaining": [
            "Verify exact Gyan FFmpeg 7.1 dependency revisions, patches and corresponding build materials (base FFmpeg alone is insufficient).",
            "Verify PyMuPDF/MuPDF native source and complete build materials, including MuPDF dependencies.",
            "Verify pillow-heif wheel native licenses, compatibility and complete matching build materials.",
            "Collect matching LibreOffice source, external components and build materials for the copied installation.",
            "Review native dependencies embedded in Pillow/lxml and Microsoft runtime redistribution terms.",
            "Complete a fresh Windows install/sign-in/offline/gesture/upgrade/uninstall acceptance run.",
        ],
    }
    (OUTPUT / "source-review.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Source review materials: " + str(OUTPUT), flush=True)
    print("Public installer remains blocked; archive collection is not a compliance sign-off.", flush=True)
    return 1 if any(item.get("status") == "blocked" for item in packages + upstream) else 0


if __name__ == "__main__":
    raise SystemExit(main())
