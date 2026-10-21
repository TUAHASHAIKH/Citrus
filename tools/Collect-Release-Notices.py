"""Copy full upstream notices into the generated installer payload.

Source archives remain unchanged. Archive members are never executed and are
written only under generated license directories, with digest-based names.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NOTICE = re.compile(r"^(?:licen[cs]e.*|copying.*|notice.*|copyright.*|authors)(?:\.[^/]*)?$", re.IGNORECASE)
LICENSE_NAME = re.compile(r"^(?:a?gpl|lgpl|apache|bsd|mit|ftl|ofl)(?:[-_.v0-9].*)?$", re.IGNORECASE)
ARCHIVES = (".tar.gz", ".tar.xz", ".tar.bz2", ".tgz", ".tbz2", ".txz", ".tar", ".zip")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, default=ROOT / "artifacts/payload")
    parser.add_argument("--sources", type=Path, default=ROOT / "artifacts/source-review")
    args = parser.parse_args()
    destination = (args.payload / "licenses").resolve()
    destination.mkdir(parents=True, exist_ok=True)
    report = json.loads((args.sources / "source-review.json").read_text())
    records = []
    def save(stream, chain, size):
        if size > 4 * 1024 * 1024:
            raise ValueError("Notice is unexpectedly large; review before omitting: " + chain)
        data = stream.read(size + 1)
        if len(data) != size:
            raise ValueError("Truncated archive notice: " + chain)
        identity = hashlib.sha256(chain.encode("utf-8")).hexdigest()[:20]
        filename = re.sub(r"[^A-Za-z0-9_.-]", "_", chain.rsplit("/", 1)[-1])[:80] or "LICENSE"
        target = destination / "upstream" / identity / filename
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        records.append(dict(file=target.relative_to(destination).as_posix(), source=chain, sha256=hashlib.sha256(data).hexdigest()))
    def inspect(path, chain, depth=0):
        if depth > 3:
            raise ValueError("Unexpected nested source archive: " + chain)
        def member(stream, name, size):
            full = chain + "/" + name
            parts = name.replace("\\", "/").split("/")
            if (name.endswith("/.git/config") or parts[-1].lower() in (".netrc", "_netrc", ".git-credentials", ".pypirc")):
                if size > 2 * 1024 * 1024:
                    raise ValueError("Unexpectedly large source-cache credential metadata")
                data = stream.read(size)
                if re.search(rb"(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]+|authorization\s*[:=]|https://[^/@\s:]+:[^/@\s]+@)", data, re.IGNORECASE):
                    raise ValueError("Potential credentials in upstream source-cache metadata; publication requires review")
                return
            if (NOTICE.fullmatch(parts[-1]) or LICENSE_NAME.fullmatch(parts[-1]) or
                    any(part.lower() in ("license", "licence", "licenses", "licences") for part in parts[:-1])):
                save(stream, full, size)
            elif name.lower().endswith(ARCHIVES):
                # Keep nested archives on disk, not in multi-gigabyte byte arrays.
                # Only cache wrappers require recursion; source distributions can
                # include test fixture archives unrelated to their own notices.
                if "dependency-cache" not in chain or depth >= 2:
                    return
                suffix = next(suffix for suffix in ARCHIVES if name.lower().endswith(suffix))
                with tempfile.TemporaryDirectory(prefix="CitrusNotices-") as temporary:
                    nested = Path(temporary) / ("source" + suffix)
                    with nested.open("wb") as output:
                        shutil.copyfileobj(stream, output)
                    inspect(nested, full, depth + 1)
        if path.name.endswith(".zip"):
            with zipfile.ZipFile(path) as archive:
                for item in archive.infolist():
                    if not item.is_dir():
                        with archive.open(item) as stream:
                            member(stream, item.filename, item.file_size)
        else:
            with tarfile.open(path, "r|*") as archive:
                for item in archive:
                    if item.isfile():
                        stream = archive.extractfile(item)
                        if stream is not None:
                            member(stream, item.name, item.size)
    for item in report["notices"]:
        path = (args.sources / item["file"]).resolve()
        if not path.is_relative_to(args.sources.resolve()):
            raise ValueError("Unsafe package notice path")
        with path.open("rb") as stream:
            save(stream, "python/" + item["file"], path.stat().st_size)
    for item in report["native_source_archives"]:
        if item.get("status") == "blocked" or "file" not in item:
            raise ValueError("Native source collection is incomplete: " + item["name"])
        path = (args.sources / item["file"]).resolve()
        if not path.is_relative_to(args.sources.resolve()):
            raise ValueError("Unsafe native source path")
        print("Collecting notices: " + path.name, flush=True)
        inspect(path, item["name"] + "/" + path.name)
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    cache = ROOT / ".engines/compliance" / lock["dependency_sources"]["file"]
    with cache.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != lock["dependency_sources"]["sha256"]:
            raise ValueError("FFmpeg dependency source cache digest mismatch")
    print("Collecting FFmpeg dependency notices", flush=True)
    inspect(cache, "ffmpeg-dependency-cache/" + cache.name)
    (destination / "index.json").write_text(json.dumps({"description": "Full upstream notices, including the FFmpeg build cache; not every cached component is necessarily compiled into the executable.",
                                                        "ffmpeg_cache_sha256": lock["dependency_sources"]["sha256"],
                                                        "source_metadata_credential_scan_passed": True,
                                                        "notices": records}, indent=2), encoding="utf-8")
    print("Collected " + str(len(records)) + " upstream notice files", flush=True)


if __name__ == "__main__":
    main()
