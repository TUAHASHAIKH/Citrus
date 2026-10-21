"""Collect upstream source archives and notices; this does not certify compliance.

Run with Python 3.13+: python tools/Collect-Release-Sources.py
Archives are never executed or extracted. Downloads are cached and re-hashed.
"""
import concurrent.futures
import argparse
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
    global OUTPUT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--libraries", type=Path, help="Audit an existing package directory without rebuilding the installer")
    parser.add_argument("--python", type=Path, help="Interpreter matching the audited runtime")
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    OUTPUT = args.output.resolve()
    runtime = None
    if args.libraries:
        if not args.python:
            parser.error("--libraries requires --python so CPython source matches the audited runtime")
        libraries = args.libraries.resolve()
        python_exe = args.python.resolve()
    else:
        runtime = json.loads((PAYLOAD / "engine/runtime.json").read_text(encoding="utf-8-sig"))
        libraries = (PAYLOAD / "engine" / runtime["Libraries"]).resolve()
        python_exe = PAYLOAD / "engine" / runtime["Python"]
    distributions = list(importlib.metadata.distributions(path=[str(libraries)]))
    if not distributions:
        raise RuntimeError("Build the self-contained payload first.")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    notices = []
    for distribution in distributions:
        name, version = distribution.metadata["Name"], distribution.version
        for relative in distribution.files or []:
            if not any(word in str(relative).lower() for word in ["license", "licence", "copying", "notice", "copyright", "authors"]):
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
    if versions.get("pillow_heif") != "1.8.1" or versions.get("pymupdf") != "1.28.2" or versions.get("imageio_ffmpeg") not in (None, "0.6.0"):
        raise ValueError("Native source mappings require pillow-heif 1.8.1, PyMuPDF 1.28.2 and, if present, imageio-ffmpeg 0.6.0; review changed builds first.")
    if versions.get("lxml") != "6.1.3":
        raise ValueError("Review lxml native source mappings before distributing another wheel version")
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        packages = list(pool.map(package_source, items))
    import subprocess
    python_version = subprocess.check_output([str(python_exe), "--version"], text=True).strip().split()[1]
    upstream = []
    sources = [
        ("CPython", "https://www.python.org/ftp/python/{0}/Python-{0}.tar.xz".format(python_version), "Python-" + python_version + ".tar.xz"),
        ("libheif", "https://github.com/strukturag/libheif/releases/download/v1.23.6/libheif-1.23.6.tar.gz", "libheif-1.23.6.tar.gz", "4484346dc5995319dbc11e3a1c35d0a2ec46511ce370900869337fd2c7033125"),
        ("libde265", "https://github.com/strukturag/libde265/releases/download/v1.1.3/libde265-1.1.3.tar.gz", "libde265-1.1.3.tar.gz", "554228bd17788c99a7e63b37ab5634722190e6e2bf60c1dcb01cef328e133905"),
        # MSYS2 used the publisher's release tarball (not GitHub's tag archive).
        # Its generated version reports 4.3+1-e9b8812. MSYS2 also applies a patch;
        # the matching packaging snapshot must accompany this source archive.
        ("x265 release source", "https://github.com/Multicorewareinc/x265/releases/download/4.3/x265_4.3.tar.gz", "x265_4.3.tar.gz", "83c53e4c8bbb8f1e33ed59e10a7d621d1d7801ca853910c3eb41f038b8ffb121"),
        ("MuPDF with third-party sources", "https://mupdf.com/downloads/archive/mupdf-1.28.2-source.tar.gz", "mupdf-1.28.2-source.tar.gz"),
        ("pillow-heif wheel build scripts", "https://codeload.github.com/bigcat88/pillow_heif/tar.gz/0ed53791bef34eeda0182b7a26476f99445922de", "pillow-heif-build-0ed53791.tar.gz"),
        ("HEIF native packaging and patches", "https://codeload.github.com/msys2/MINGW-packages/tar.gz/7c4b39d9bb2605b63a904a031b0aec309b4ac548", "msys2-packages-7c4b39d9.tar.gz"),
        ("MinGW-w64 runtime source", "https://codeload.github.com/mingw-w64/mingw-w64/tar.gz/4564ee4b5063097bf747af3a3f8270a28adff820", "mingw-w64-4564ee4b.tar.gz"),
        ("GCC 16.2 runtime source", "https://ftp.gnu.org/gnu/gcc/gcc-16.2.0/gcc-16.2.0.tar.xz", "gcc-16.2.0.tar.xz", "e6738e29597f733270731aa90600f37ffdc045079dfc27ec7e8192cc81085c3e"),
        ("PyMuPDF exact wheel source commit", "https://codeload.github.com/pymupdf/PyMuPDF/tar.gz/12786d12b2962a1d77fd13b103162eddf749644c", "pymupdf-12786d12.tar.gz"),
    ]
    # The Windows wheel CI cache job 100199111245 (run 33615138284)
    # records these exact 2026.05.17 static dependencies. The build repository
    # supplies patches and the submodule commits below, including LGPL iconv.
    sources.extend([
        ("lxml native build scripts and patches", "https://codeload.github.com/lxml/libxml2-win-binaries/tar.gz/4e8ae01f61145dc823ce2ae1d79f06241b7b46de", "lxml-native-build-4e8ae01f.tar.gz"),
        ("lxml libiconv 1.17.1 source", "https://codeload.github.com/winlibs/libiconv/tar.gz/880a1fa8b5581e37e136a7b051947d3ea39097b6", "libiconv-880a1fa8.tar.gz"),
        ("lxml libxml2 2.11.9 source", "https://codeload.github.com/winlibs/libxml2/tar.gz/bb84678855e3407fc88180295c992f9c984e28e0", "libxml2-bb846788.tar.gz"),
        ("lxml libxslt 1.1.45 source", "https://codeload.github.com/winlibs/libxslt/tar.gz/c0076eaa6e0d1f99e01107e8db225a308eb1e543", "libxslt-c0076eaa.tar.gz"),
        ("lxml zlib 1.3.2 source", "https://codeload.github.com/winlibs/zlib/tar.gz/0089522e62e6b5d4950e62bd20f0cb06a9288413", "zlib-0089522e.tar.gz"),
        ("Inno Setup 6.7.3 source", "https://codeload.github.com/jrsoftware/issrc/tar.gz/4adf37ed7f3fd2bd11c6836ba056e3de170fbabf", "inno-setup-6.7.3.tar.gz"),
    ])
    if "imageio_ffmpeg" in versions:
        sources.append(("FFmpeg base source (not the complete Gyan build)", "https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/refs/tags/n7.1", "ffmpeg-7.1.tar.gz"))
    else:
        lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
        sources.extend([
            ("FFmpeg exact source", "https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/" + lock["ffmpeg_commit"], "ffmpeg-" + lock["ffmpeg_commit"] + ".tar.gz"),
            ("FFmpeg build scripts", "https://codeload.github.com/" + lock["publisher"] + "/tar.gz/" + lock["build_scripts_commit"], "ffmpeg-build-scripts-" + lock["build_scripts_commit"] + ".tar.gz"),
        ])
    def native_source(item):
        name, url, filename, *expected = item
        try:
            result = dict(name=name, **download(url, OUTPUT / "native-sources" / filename, expected[0] if expected else None))
            print(name + ": archive collected", flush=True)
        except Exception as error:
            result = dict(name=name, status="blocked", error=str(error), url=url)
            print(name + ": " + str(error), flush=True)
        return result
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        upstream = list(pool.map(native_source, sources))
    report = {
        "collection_complete": not any(item.get("status") == "blocked" for item in packages + upstream),
        "scope": "package-directory audit" if args.libraries else "installer payload audit",
        "packages": packages, "notices": notices, "native_source_archives": upstream,
        "next_check": "Run Verify-Release-Sources.py against the actual installer payload after Office and FFmpeg dependency sources finish collecting.",
        "legacy_ffmpeg_provenance_unresolved": "imageio_ffmpeg" in versions,
    }
    (OUTPUT / "source-review.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Source review materials: " + str(OUTPUT), flush=True)
    print("Collection complete: " + str(report["collection_complete"]) + "; verify corresponding sources against the installer payload before packaging.", flush=True)
    return 1 if any(item.get("status") == "blocked" for item in packages + upstream) else 0


if __name__ == "__main__":
    raise SystemExit(main())
