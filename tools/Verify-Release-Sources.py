"""Verify source materials against an actual offline payload, then index them.

This checks files and provenance mappings, not legal eligibility or clean-PC
acceptance. Run after both source collectors have completed for this payload.
"""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def normalized(name):
    return re.sub(r"[-_.]+", "-", name).lower()


def checked_path(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()) or not path.is_file():
        raise ValueError("Missing or unsafe source material: " + str(relative))
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, default=ROOT / "artifacts/payload")
    parser.add_argument("--sources", type=Path, default=ROOT / "artifacts/source-review")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/setup/source-materials.json")
    args = parser.parse_args()
    payload, sources = args.payload.resolve(), args.sources.resolve()
    runtime = json.loads((payload / "engine/runtime.json").read_text(encoding="utf-8-sig"))
    build_info = json.loads((payload / "build-info.json").read_text(encoding="utf-8-sig"))
    for source in build_info["SourceFiles"]:
        path = checked_path(ROOT, source["File"])
        if digest(path) != source["SHA256"]:
            raise ValueError("Payload was built from different Citrus source: " + source["File"])
    if digest(payload / "engine/worker.py") != digest(ROOT / "engines/worker.py"):
        raise ValueError("Bundled conversion worker differs from the corresponding source")
    libraries = (payload / "engine" / runtime["Libraries"]).resolve()
    if not libraries.is_relative_to(payload):
        raise ValueError("The payload references an external development environment")
    actual = {(normalized(d.metadata["Name"]), d.version) for d in importlib.metadata.distributions(path=[str(libraries)])}
    if any(name == "imageio-ffmpeg" for name, _ in actual) or list(libraries.rglob("opencv_videoio_ffmpeg*.dll")):
        raise ValueError("Payload still includes a superseded FFmpeg distribution")
    report = json.loads((sources / "source-review.json").read_text())
    if report.get("scope") != "installer payload audit":
        raise ValueError("Source inventory audits development libraries, not this installer payload; rerun Collect-Release-Sources.py without --libraries")
    recorded = {(normalized(p["name"]), p["version"]) for p in report["packages"]}
    if actual != recorded:
        raise ValueError("Python source inventory does not match the payload: " + str(actual.symmetric_difference(recorded)))
    archives = []
    def verify(root, record, component):
        if record.get("status") == "blocked" or "file" not in record:
            raise ValueError("Source collection incomplete: " + component)
        path = checked_path(root, record["file"])
        if digest(path) != record["sha256"]:
            raise ValueError("Source material digest changed: " + record["file"])
        archives.append(dict(component=component, file=path.relative_to(ROOT).as_posix(), sha256=record["sha256"],
                             size=path.stat().st_size, upstream_url=record.get("url")))
    for package in report["packages"]:
        verify(sources, package, package["name"] + " " + package["version"])
    required = {"CPython", "libheif", "libde265", "x265 release source", "MuPDF with third-party sources",
                "pillow-heif wheel build scripts", "HEIF native packaging and patches", "MinGW-w64 runtime source",
                "GCC 16.2 runtime source", "PyMuPDF exact wheel source commit", "FFmpeg exact source", "FFmpeg build scripts",
                "lxml native build scripts and patches", "lxml libiconv 1.17.1 source",
                "lxml libxml2 2.11.9 source", "lxml libxslt 1.1.45 source", "lxml zlib 1.3.2 source",
                "Inno Setup 6.7.3 source"}
    native = {item["name"]: item for item in report["native_source_archives"]}
    if not required.issubset(native):
        raise ValueError("Missing native source mappings: " + str(sorted(required - native.keys())))
    for name in sorted(required):
        verify(sources, native[name], name)
    python = checked_path(payload / "engine", runtime["Python"])
    version = subprocess.check_output([str(python), "--version"], text=True).strip().split()[1]
    if Path(native["CPython"]["file"]).name != "Python-" + version + ".tar.xz":
        raise ValueError("CPython source does not match the embedded interpreter")
    for notice in report["notices"]:
        path = checked_path(sources, notice["file"])
        if digest(path) != notice["sha256"]:
            raise ValueError("Package notice changed: " + notice["file"])
    office = sources / "office"
    office_report = json.loads((office / "manifest.json").read_text())
    if not office_report.get("complete"):
        raise ValueError("LibreOffice source collection is incomplete")
    office_exe = checked_path(payload / "engine", runtime["Office"])
    version_ini = office_exe.with_name("version.ini").read_text(encoding="utf-8-sig")
    build = re.search(r"^buildid=(\w+)", version_ini, re.MULTILINE | re.IGNORECASE)
    if not build or build[1] != office_report["source_commit"]:
        raise ValueError("LibreOffice source commit does not match the installed build ID")
    for record in office_report["archives"]:
        verify(office, record, "LibreOffice " + record["component"])
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    cache = ROOT / ".engines/compliance"
    for item in (lock["binary"], lock["dependency_sources"]):
        path = checked_path(cache, item["file"])
        if digest(path) != item["sha256"]:
            raise ValueError("Pinned FFmpeg artifact digest mismatch: " + item["file"])
    binary = checked_path(payload / "engine", runtime["FFmpeg"])
    with zipfile.ZipFile(cache / lock["binary"]["file"]) as archive:
        members = [item for item in archive.namelist() if item.endswith("/bin/ffmpeg.exe")]
        if len(members) != 1:
            raise ValueError("Unexpected FFmpeg publisher archive layout")
        with archive.open(members[0]) as stream:
            expected = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest(binary) != expected:
            raise ValueError("Payload FFmpeg differs from the pinned publisher build")
    dependency = cache / lock["dependency_sources"]["file"]
    archives.append(dict(component="FFmpeg dependency source cache", file=dependency.relative_to(ROOT).as_posix(),
                         sha256=lock["dependency_sources"]["sha256"], size=dependency.stat().st_size))
    result = {"material_checks_passed": True, "scope": "Source files and pinned binary provenance mappings",
              "note": "File checks do not establish Microsoft redistribution eligibility, source hosting availability or clean-Windows acceptance.",
              "python_version": version, "ffmpeg_build": lock, "libreoffice_build": build[1], "archives": archives,
              "citrus_build": build_info,
              "payload_executable_sha256": digest(payload / "Citrus.exe")}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("Verified source material inventory: " + str(args.output))


if __name__ == "__main__":
    main()
