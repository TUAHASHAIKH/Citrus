"""Package verified corresponding sources into independently extractable ZIPs.

The publisher's large FFmpeg cache is kept byte-for-byte in numbered parts.
Nothing is published by this command. Run from a clean, committed source tree.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LIMIT = 900 * 1024 * 1024
PART = 768 * 1024 * 1024


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", type=Path, default=ROOT / "artifacts/setup/source-materials.json")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/source-release")
    args = parser.parse_args()
    output = args.output.resolve()
    if not output.is_relative_to((ROOT / "artifacts").resolve()):
        raise ValueError("Source packages must be generated inside artifacts")
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip():
        raise ValueError("Commit reviewed source changes before exporting a corresponding-source release")
    inventory = json.loads(args.inventory.read_text())
    if not inventory.get("material_checks_passed"):
        raise ValueError("Verify the actual payload's corresponding sources first")
    if digest(ROOT / "artifacts/payload/Citrus.exe") != inventory["payload_executable_sha256"]:
        raise ValueError("Installer payload changed after source verification")
    for item in inventory["citrus_build"]["SourceFiles"]:
        path = (ROOT / item["File"]).resolve()
        if not path.is_relative_to(ROOT) or digest(path) != item["SHA256"]:
            raise ValueError("Citrus source changed after the verified build")
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    notices = json.loads((ROOT / "artifacts/payload/licenses/index.json").read_text())
    if (not notices.get("source_metadata_credential_scan_passed") or
            notices.get("ffmpeg_cache_sha256") != lock["dependency_sources"]["sha256"]):
        raise ValueError("Finish the full source-cache notice and metadata audit before packaging")
    output.mkdir(parents=True, exist_ok=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    version = "0.3.2"
    records = []
    files = []
    runtime_terms = ROOT / "artifacts/payload/licenses/microsoft/Microsoft-VC14-license.docx"
    if digest(runtime_terms) != "08651651a7602fc7c0e2763de0fde1ff9f868df2780597cd1775ee9d6441c783":
        raise ValueError("The retained runtime terms differ from the publisher document")
    files.append((runtime_terms, "build-inputs/Microsoft-VC14-license.docx"))
    files.append((args.inventory, "provenance/source-materials.json"))
    # Keep the content-addressed original archives, not re-created substitutes.
    for item in inventory["archives"]:
        path = (ROOT / item["file"]).resolve()
        if not path.is_relative_to(ROOT) or digest(path) != item["sha256"]:
            raise ValueError("Source archive changed: " + item["file"])
        if item["component"] == "FFmpeg dependency source cache":
            continue
        relative = "archives/" + path.relative_to(ROOT / "artifacts/source-review").as_posix()
        files.append((path, relative))
        records.append(dict(item, packaged_file=relative))
    with tempfile.TemporaryDirectory(prefix="CitrusSourcePackage-", dir=ROOT / "artifacts") as temporary:
        stage = Path(temporary)
        source = stage / ("Citrus-" + version + "-source.tar.gz")
        subprocess.run(["git", "archive", "--format=tar.gz", "-o", str(source), commit], cwd=ROOT, check=True)
        files.append((source, source.name))
        records.append(dict(component="Citrus source and build scripts", packaged_file=source.name, sha256=digest(source), git_commit=commit))
        dependency = ROOT / ".engines/compliance" / lock["dependency_sources"]["file"]
        if digest(dependency) != lock["dependency_sources"]["sha256"]:
            raise ValueError("FFmpeg dependency source cache changed")
        parts = []
        with dependency.open("rb") as source_stream:
            index = 1
            while True:
                first = source_stream.read(1024 * 1024)
                if not first:
                    break
                name = dependency.name + "." + str(index).zfill(3)
                target = output / name
                total = len(first)
                with target.open("wb") as stream:
                    stream.write(first)
                    while total < PART:
                        chunk = source_stream.read(min(1024 * 1024, PART - total))
                        if not chunk:
                            break
                        stream.write(chunk); total += len(chunk)
                parts.append(dict(file=name, size=total, sha256=digest(target)))
                index += 1
        join = r'''# Reassemble the unmodified upstream source cache; no archives are executed.
$ErrorActionPreference = 'Stop'
$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SOURCE_MANIFEST.json') -Raw | ConvertFrom-Json
$target = Join-Path $PSScriptRoot 'ffmpeg-dependency-cache.zip'
$temporary = $target + '.partial'
$output = [IO.File]::Create($temporary)
try {
    foreach ($part in $metadata.ffmpeg_dependency_cache.parts) {
        $path = Join-Path $PSScriptRoot $part.file
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $part.sha256) { throw 'Source part digest mismatch' }
        $inputStream = [IO.File]::OpenRead($path)
        try { $inputStream.CopyTo($output) } finally { $inputStream.Dispose() }
    }
} finally { $output.Dispose() }
if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.ffmpeg_dependency_cache.sha256) { throw 'Reassembled source cache digest mismatch' }
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Host "Verified source cache: $target"
'''
        (output / "Join-FFmpeg-Source-Cache.ps1").write_text(join, encoding="utf-8-sig")
        instructions = ("Citrus " + version + " corresponding sources\n\n"
            "Download and verify all Citrus-*-Sources-*.zip files using SHA256SUMS.txt. Extract every ZIP into the same empty folder.\n"
            "Download all ffmpeg-dependency-cache.zip.00* parts, SOURCE_MANIFEST.json and Join-FFmpeg-Source-Cache.ps1 into one folder.\n"
            "Run powershell -ExecutionPolicy Bypass -File .\\Join-FFmpeg-Source-Cache.ps1 to reconstruct the exact source cache.\n"
            "The numbered parts are source materials, not installer dependencies. Installing and converting with Citrus stays offline.\n\n"
            "Citrus: extract Citrus-" + version + "-source.tar.gz. The complete source, assets and Windows build tools are included.\n"
            "Read README.md and THIRD_PARTY_NOTICES.md. A suitably licensed Visual Studio edition is needed to redistribute Microsoft's runtime.\n"
            "Place the reconstructed cache in .engines/compliance/ffmpeg-dependency-cache.zip before rebuilding the offline installer.\n\n"
            "FFmpeg: extract its pinned build-scripts archive. Extract cache.tar.gz from the reconstructed cache ZIP, then extract that TAR in the build-scripts root to restore .cache/downloads.\n"
            "Use Bash and Docker with the included publisher scripts: ./makeimage.sh win64 gpl 8.1 and ./build.sh win64 gpl 8.1.\n"
            "Pin the source selection before building: copy build.sh to build-citrus.sh and insert 'git checkout --detach 46d899d7a02bc63237472cc4897258d5ddf274ac' immediately after its 'cd ffmpeg' line. Run ./build-citrus.sh win64 gpl 8.1 instead of ./build.sh.\n"
            "The exact FFmpeg source archive is also provided; an offline source build can replace the clone step with extraction of that archive into ffbuild/ffmpeg.\n"
            "The cache includes the exact fetched dependency sources and the scripts contain the patches/configuration.\n\n"
            "HEIC: use pillow-heif's pinned Windows workflow and libheif/windows/mingw-w64-libheif/PKGBUILD.\n"
            "The MSYS2 snapshot includes native compiler/runtime recipes and patches. It used GCC 16.2.0-5, x265 4.3-1 (4.3+1-e9b8812), libde265 1.1.3-1 and MinGW-w64 4564ee4b5063097bf747af3a3f8270a28adff820.\n"
            "PyMuPDF: its exact wrapper commit, setup.py and MuPDF 1.28.2 full source archive are included. The native archive includes thirdparty source trees. The wheel reports SWIG 4.4.1.\n"
            "LibreOffice: extract the four official release archives. Its external sources are in archives/office/external; patches and Windows distro configurations are in the core source archive.\n"
            "lxml: its native build repository includes build.ps1 and libiconv.patch. Restore libiconv, libxml2, libxslt and zlib subdirectories from the supplied exact submodule archives, then follow the Windows build workflow.\n"
            "The complete lxml and native sources/build files permit rebuilding with modified iconv; retain LGPL notices.\n"
            "Inno Setup: version 6.7.3 source and publisher license are included; see its build documentation for Delphi/compiler requirements.\n"
            "Python libraries: use the exact-version source distributions and their build files. CPython source is supplied separately.\n\n"
            "Upstream licenses remain attached to their respective source archives. Microsoft's runtime is proprietary and is not relicensed as AGPL.\n"
            "Byte-identical rebuilds are not claimed; standard compilers, SDKs and platform tools may also be required.\n")
        (output / "BUILDING-SOURCES.txt").write_text(instructions, encoding="utf-8")
        # Each ZIP is independent, and remains comfortably below GitHub's asset limit.
        bundles = []
        archive = None
        try:
            size = 0
            for path, relative in files:
                if path.stat().st_size > LIMIT:
                    raise ValueError("Source archive requires explicit splitting: " + path.name)
                if archive is None or size + path.stat().st_size > LIMIT:
                    if archive:
                        archive.close()
                    name = "Citrus-" + version + "-Sources-" + str(len(bundles) + 1).zfill(2) + ".zip"
                    target = output / name
                    bundles.append(target)
                    archive = zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED)
                    size = 0
                archive.write(path, relative)
                size += path.stat().st_size
        finally:
            if archive:
                archive.close()
        bundle_records = [dict(file=p.name, size=p.stat().st_size, sha256=digest(p)) for p in bundles]
        manifest = dict(version=version, git_commit=commit, payload_executable_sha256=inventory["payload_executable_sha256"],
            ffmpeg_build=lock, archives=records, source_bundles=bundle_records,
            ffmpeg_dependency_cache=dict(sha256=lock["dependency_sources"]["sha256"], parts=parts))
        (output / "SOURCE_MANIFEST.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    assets = [output / item["file"] for item in bundle_records + parts]
    assets += [output / name for name in ("SOURCE_MANIFEST.json", "BUILDING-SOURCES.txt", "Join-FFmpeg-Source-Cache.ps1")]
    # Also retain the exact engine binary: the publisher deletes daily builds.
    binary = ROOT / ".engines/compliance" / lock["binary"]["file"]
    if digest(binary) != lock["binary"]["sha256"]:
        raise ValueError("Pinned FFmpeg rebuild input changed")
    assets.append(binary)
    (output / "SHA256SUMS.txt").write_text("".join(digest(p) + "  " + p.name + "\n" for p in assets), encoding="ascii")
    print("Prepared " + str(len(assets)) + " source-release assets in " + str(output))


if __name__ == "__main__":
    main()
