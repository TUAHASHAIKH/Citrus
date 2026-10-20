"""Record the actual prepared engine versions and binary hashes for release review."""
import hashlib
import importlib.metadata
import json
from pathlib import Path
import subprocess
import sys

payload = Path(sys.argv[1]).resolve()
runtime = json.loads((payload / "engine/runtime.json").read_text(encoding="utf-8-sig"))
libraries = payload / "engine" / runtime["Libraries"]
packages = []
for distribution in importlib.metadata.distributions(path=[str(libraries)]):
    packages.append({
        "name": distribution.metadata["Name"], "version": distribution.version,
        "license": distribution.metadata.get("License-Expression") or distribution.metadata.get("License", "See bundled metadata"),
        "notice_files": [str(p).replace("\\", "/") for p in (distribution.files or [])
                         if any(part in str(p).lower() for part in ["license", "copying", "notice"])],
    })
ffmpeg = payload / "engine" / runtime["FFmpeg"]
def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()
report = {
    "citrus_version": "0.3.1", "runtime": runtime,
    "python_version": subprocess.check_output([str(payload / "engine" / runtime["Python"]), "--version"], text=True).strip(),
    "packages": sorted(packages, key=lambda item: item["name"].lower()),
    "ffmpeg_build": subprocess.check_output([str(ffmpeg), "-version"], text=True, errors="replace"),
    "binary_sha256": {str(path.relative_to(payload)).replace("\\", "/"): digest(path)
                      for path in [payload / "Citrus.exe", ffmpeg, payload / "engine" / runtime["Office"]]},
    "public_release_status": "Release candidate: corresponding-source bundle, signing and clean-VM acceptance remain required."
}
print(json.dumps(report, indent=2))
