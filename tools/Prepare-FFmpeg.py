"""Stage a pinned offline FFmpeg replacement and its upstream build sources.

Does not modify the running app or authorize release distribution. The large
dependency cache is obtained separately with Fetch-GitHub-Artifact.py.
"""
import argparse
import re
import shutil
import urllib.request
import importlib.util
import json
from pathlib import Path
import zipfile
import urllib.error

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("collector", Path(__file__).with_name("Collect-Release-Sources.py"))
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


def restore_dependency_cache(lock, cache):
    dependency = lock["dependency_sources"]
    target = cache / dependency["file"]
    if target.is_file() and collector.sha256(target) == dependency["sha256"]:
        return
    base = lock["persistent_release"]
    with urllib.request.urlopen(base + "SOURCE_MANIFEST.json", timeout=45) as response:
        record = json.load(response)["ffmpeg_dependency_cache"]
    if record["sha256"] != dependency["sha256"] or not record["parts"]:
        raise ValueError("Source mirror does not match the pinned cache digest")
    temporary = target.with_name(target.name + ".partial")
    try:
        with temporary.open("wb") as output:
            for index, part in enumerate(record["parts"], 1):
                expected = dependency["file"] + "." + str(index).zfill(3)
                if part["file"] != expected or not re.fullmatch(r"[0-9a-f]{64}", part["sha256"]):
                    raise ValueError("Invalid source mirror part metadata")
                path = cache / expected
                collector.download(base + expected, path, part["sha256"])
                if path.stat().st_size != part["size"]:
                    raise ValueError("Source mirror part size mismatch")
                with path.open("rb") as source:
                    shutil.copyfileobj(source, output)
        if collector.sha256(temporary) != dependency["sha256"]:
            raise ValueError("Reassembled source mirror digest mismatch")
        temporary.replace(target)
        for part in record["parts"]:
            (cache / part["file"]).unlink()
    finally:
        temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--with-sources", action="store_true", help="Restore pinned sources from the persistent release if missing")
    args = parser.parse_args()
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    cache = ROOT / ".engines/compliance"
    collector.OUTPUT = cache
    base = "https://github.com/" + lock["publisher"] + "/releases/download/" + lock["release"] + "/"
    binary = lock["binary"]
    try:
        record = collector.download(base + binary["file"], cache / binary["file"], binary["sha256"])
    except urllib.error.HTTPError as error:
        if error.code not in (404, 410):
            raise
        record = collector.download(lock["persistent_release"] + binary["file"], cache / binary["file"], binary["sha256"])
    print("Verified pinned FFmpeg binary archive", flush=True)
    records = [record]
    for name, repo, commit in (("ffmpeg", "FFmpeg/FFmpeg", lock["ffmpeg_commit"]),
                               ("ffmpeg-build-scripts", lock["publisher"], lock["build_scripts_commit"])):
        records.append(collector.download("https://codeload.github.com/" + repo + "/tar.gz/" + commit,
                                         cache / (name + "-" + commit + ".tar.gz")))
        print("Collected " + name, flush=True)
    target = cache / "ffmpeg"
    target.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(cache / binary["file"]) as archive:
        # Stage only the converter and upstream documentation, preserving paths.
        # Reject unsafe names even though the publisher digest is pinned.
        for member in archive.infolist():
            relative = Path(*Path(member.filename).parts[1:])
            destination = (target / relative).resolve()
            if not destination.is_relative_to(target.resolve()):
                raise ValueError("Unsafe FFmpeg archive path")
            if member.is_dir() or (relative.suffix == ".exe" and relative.name != "ffmpeg.exe"):
                continue
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, destination.open("wb") as output:
                import shutil
                shutil.copyfileobj(source, output)
    if args.with_sources:
        restore_dependency_cache(lock, cache)
    dependency = lock["dependency_sources"]
    source_cache = cache / dependency["file"]
    dependency_verified = source_cache.exists() and collector.sha256(source_cache) == dependency["sha256"]
    (cache / "ffmpeg-staging.json").write_text(json.dumps({"binary_and_build_sources": records,
        "dependency_cache_verified": dependency_verified, "release_review_complete": False}, indent=2), encoding="utf-8")
    print("FFmpeg staged; dependency source cache verified: " + str(dependency_verified), flush=True)


if __name__ == "__main__":
    main()
