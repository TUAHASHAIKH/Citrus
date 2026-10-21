"""Upload the reviewed source-release assets to Citrus's GitHub release.

Uses the existing Git credential helper in memory. Credentials are sent only to
api.github.com and uploads.github.com; redirects are never followed with them.
No installer, personal files or arbitrary directory contents are uploaded.
"""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "TUAHASHAIKH/Citrus"
TAG = "citrus-sources-0.3.2"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=ROOT / "artifacts/source-release")
    parser.add_argument("--publish", action="store_true", help="Publish after all server digests match; otherwise keep a draft")
    args = parser.parse_args()
    folder = args.assets.resolve()
    manifest = json.loads((folder / "SOURCE_MANIFEST.json").read_text())
    lock = json.loads((ROOT / "engines/ffmpeg-release.json").read_text())
    expected_names = {x["file"] for x in manifest["source_bundles"] + manifest["ffmpeg_dependency_cache"]["parts"]}
    for name in expected_names:
        if not re.fullmatch(r"(?:Citrus-0\.3\.2-Sources-\d{2}\.zip|ffmpeg-dependency-cache\.zip\.\d{3})", name):
            raise ValueError("Unexpected source-release asset filename")
    expected_names.update(("SOURCE_MANIFEST.json", "BUILDING-SOURCES.txt", "Join-FFmpeg-Source-Cache.ps1", lock["binary"]["file"]))
    sums = {}
    for line in (folder / "SHA256SUMS.txt").read_text().splitlines():
        expected, name = line.split("  ", 1)
        if not re.fullmatch(r"[a-f0-9]{64}", expected) or not re.fullmatch(r"[A-Za-z0-9_.-]+", name):
            raise ValueError("Invalid source asset checksum record")
        if name in sums:
            raise ValueError("Duplicate source-release asset")
        sums[name] = expected
    if set(sums) != expected_names:
        raise ValueError("Asset list differs from the reviewed source manifest")
    assets = []
    for name, expected in sums.items():
        path = (ROOT / ".engines/compliance" / name) if name == lock["binary"]["file"] else folder / name
        if digest(path) != expected:
            raise ValueError("Source asset digest mismatch: " + name)
        if path.stat().st_size >= 2 * 1024**3:
            raise ValueError("Source asset exceeds GitHub's per-file limit")
        assets.append((path, expected))
    assets.append((folder / "SHA256SUMS.txt", digest(folder / "SHA256SUMS.txt")))
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        credential = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
            text=True, capture_output=True, env=dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="Never"))
        fields = dict(line.split("=", 1) for line in credential.stdout.splitlines() if "=" in line)
        token = fields.get("password") if credential.returncode == 0 else None
    if not token:
        raise RuntimeError("GitHub authentication is unavailable")
    opener = urllib.request.build_opener(NoRedirect)
    def api(method, path, data=None, allow_missing=False):
        request = urllib.request.Request("https://api.github.com/repos/" + REPOSITORY + path,
            data=json.dumps(data).encode() if data is not None else None, method=method,
            headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json", "Content-Type": "application/json",
                     "User-Agent": "Citrus-corresponding-source-release"})
        try:
            with opener.open(request, timeout=90) as response:
                body = response.read()
                return json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            if allow_missing and error.code == 404:
                return None
            raise RuntimeError("GitHub source-release request failed (HTTP " + str(error.code) + ")") from None
    release = api("GET", "/releases/tags/" + TAG, allow_missing=True)
    if release is None:
        drafts = [item for item in api("GET", "/releases?per_page=100") if item["tag_name"] == TAG]
        if len(drafts) > 1:
            raise ValueError("Multiple source drafts use this tag")
        release = drafts[0] if drafts else None
    body = ("Corresponding sources and retained engine build input for the Citrus 0.3.2 offline installer candidate.\n\n"
            "Citrus source commit: " + manifest["git_commit"] + ".\n\n"
            "Download all Sources ZIPs and the numbered FFmpeg source-cache parts. See BUILDING-SOURCES.txt for extraction and rebuilding, "
            "SOURCE_MANIFEST.json for provenance, and SHA256SUMS.txt for integrity. The FFmpeg executable archive is retained because its publisher expires daily builds.\n\n"
            "Upstream licenses remain attached to their respective components. This release archives sources/build input; the Citrus installer is not uploaded here. "
            "Fresh Windows acceptance testing is still pending.")
    if release is None:
        release = api("POST", "/releases", dict(tag_name=TAG, target_commitish=manifest["git_commit"],
            name="Citrus 0.3.2 corresponding sources and engine archive", body=body, draft=True, prerelease=True))
    existing = {item["name"]: item for item in api("GET", "/releases/" + str(release["id"]) + "/assets?per_page=100")}
    for path, expected in assets:
        found = existing.get(path.name)
        if found:
            if found.get("digest") == "sha256:" + expected and found["size"] == path.stat().st_size:
                print("Verified existing source asset: " + path.name, flush=True)
                continue
            if release["draft"] and found.get("state") != "uploaded":
                api("DELETE", "/releases/assets/" + str(found["id"]))
                print("Removed incomplete upload from the source draft: " + path.name, flush=True)
            else:
                raise ValueError("An existing release asset differs; refusing to overwrite it: " + path.name)
        if not release["draft"]:
            raise ValueError("Refusing to modify an already published source release")
        print("Uploading source asset: " + path.name, flush=True)
        connection = http.client.HTTPSConnection("uploads.github.com", timeout=120, blocksize=1024 * 1024)
        try:
            with path.open("rb") as stream:
                connection.request("POST", "/repos/" + REPOSITORY + "/releases/" + str(release["id"]) + "/assets?name=" + urllib.parse.quote(path.name),
                    body=stream, headers={"Authorization": "Bearer " + token, "User-Agent": "Citrus-corresponding-source-release",
                        "Content-Type": "application/octet-stream", "Content-Length": str(path.stat().st_size)})
                response = connection.getresponse()
                if response.status != 201:
                    raise RuntimeError("GitHub source upload failed (HTTP " + str(response.status) + ")")
                result = json.loads(response.read(2 * 1024 * 1024))
        finally:
            connection.close()
        if result.get("digest") != "sha256:" + expected or result["size"] != path.stat().st_size:
            raise ValueError("GitHub did not confirm the expected source asset digest")
        print("Verified uploaded source asset: " + path.name, flush=True)
    final = api("GET", "/releases/" + str(release["id"]) + "/assets?per_page=100")
    hashes = {item["name"]: item.get("digest") for item in final}
    if any(hashes.get(path.name) != "sha256:" + expected for path, expected in assets):
        raise ValueError("The final GitHub asset inventory differs from local source materials")
    if args.publish and release["draft"]:
        release = api("PATCH", "/releases/" + str(release["id"]), dict(draft=False, prerelease=True, body=body))
    (folder / "github-source-release.json").write_text(json.dumps({"url": release["html_url"], "draft": release["draft"],
        "git_commit": manifest["git_commit"], "verified_asset_digests": hashes}, indent=2), encoding="utf-8")
    print("Source release: " + release["html_url"] + " (draft=" + str(release["draft"]) + ")", flush=True)


if __name__ == "__main__":
    main()
