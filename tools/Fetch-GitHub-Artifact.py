"""Download a large upstream source artifact using the existing Git credential helper.

Credentials stay in memory and are sent only to api.github.com. The signed
artifact redirect is downloaded without forwarding the authorization header.
"""
import argparse
import concurrent.futures
import hashlib
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("repository")
    parser.add_argument("artifact", type=int)
    parser.add_argument("output", type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--workers", type=int, default=8, choices=range(1, 17))
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository) or not re.fullmatch(r"[a-f0-9]{64}", args.sha256):
        raise ValueError("Invalid repository or expected SHA-256")
    if args.output.exists():
        with args.output.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() == args.sha256:
                print("Verified cached source artifact.")
                return
    environment = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="Never")
    credential = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                                text=True, capture_output=True, env=environment)
    fields = dict(line.split("=", 1) for line in credential.stdout.splitlines() if "=" in line)
    if credential.returncode or not fields.get("password"):
        raise RuntimeError("GitHub credential helper is not available for the upstream source artifact.")
    redirect_lock = threading.Lock()
    redirect = {"target": None, "created": 0}
    def storage_target(refresh=False):
        with redirect_lock:
            if not refresh and redirect["target"] and time.monotonic() - redirect["created"] < 120:
                return redirect["target"]
            request = urllib.request.Request(
                f"https://api.github.com/repos/{args.repository}/actions/artifacts/{args.artifact}/zip?citrus_request={time.time_ns()}",
                headers={"Authorization": "Bearer " + fields["password"], "User-Agent": "Citrus-source-audit", "Cache-Control": "no-cache"})
            try:
                urllib.request.build_opener(NoRedirect).open(request, timeout=60)
                raise RuntimeError("Expected GitHub's artifact storage redirect")
            except urllib.error.HTTPError as error:
                if error.code != 302:
                    raise RuntimeError("GitHub artifact access failed (HTTP " + str(error.code) + ")") from None
                target = error.headers["Location"]
            parsed = urllib.parse.urlparse(target)
            if parsed.scheme != "https" or not parsed.hostname or not (parsed.hostname.endswith(".blob.core.windows.net") or parsed.hostname.endswith(".githubusercontent.com")):
                raise RuntimeError("Unexpected artifact storage host")
            redirect.update(target=target, created=time.monotonic())
            return target
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".partial")
    # Azure artifact storage supports ranges. Independent parts avoid restarting
    # multi-gigabyte transfers after a connection timeout. Only the final upstream
    # SHA-256 can promote the assembled archive into the verified cache.
    with urllib.request.urlopen(urllib.request.Request(storage_target(), method="HEAD"), timeout=60) as response:
        length = int(response.headers["Content-Length"])
        etag = response.headers.get("ETag")
    parts = args.output.with_suffix(args.output.suffix + ".parts")
    parts.mkdir(parents=True, exist_ok=True)
    part_size = 32 * 1024 * 1024
    def fetch_part(index):
        start = index * part_size
        end = min(length, start + part_size) - 1
        path = parts / (str(index).zfill(5) + ".part")
        if path.exists() and path.stat().st_size == end - start + 1:
            return path
        for attempt in range(5):
            try:
                headers = {"Range": f"bytes={start}-{end}"}
                if etag:
                    headers["If-Range"] = etag
                request = urllib.request.Request(storage_target(refresh=attempt > 0), headers=headers)
                with urllib.request.urlopen(request, timeout=60) as response, path.open("wb") as stream:
                    if response.status != 206 or response.headers.get("Content-Range") != f"bytes {start}-{end}/{length}":
                        raise RuntimeError("Artifact server did not honor the requested source range")
                    while chunk := response.read(1024 * 1024):
                        stream.write(chunk)
                if path.stat().st_size != end - start + 1:
                    raise RuntimeError("Incomplete source artifact range")
                return path
            except Exception as error:
                path.unlink(missing_ok=True)
                reason = "HTTP " + str(error.code) if isinstance(error, urllib.error.HTTPError) else type(error).__name__
                print(f"Retrying source part {index}: {reason}", flush=True)
                if attempt == 4:
                    raise RuntimeError("Source artifact part download failed; rerun to resume completed parts") from None
                time.sleep(attempt + 1)
    count = (length + part_size - 1) // part_size
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(fetch_part, index) for index in range(count)]
        for done, future in enumerate(concurrent.futures.as_completed(futures), 1):
            future.result()
            print(f"Source artifact: {done}/{count} parts complete", flush=True)
    digest = hashlib.sha256(); total = 0
    try:
        with temporary.open("wb") as stream:
            for index in range(count):
                with (parts / (str(index).zfill(5) + ".part")).open("rb") as source:
                    while chunk := source.read(1024 * 1024):
                        digest.update(chunk); stream.write(chunk); total += len(chunk)
        if digest.hexdigest() != args.sha256:
            raise RuntimeError("Upstream artifact SHA-256 mismatch")
        temporary.replace(args.output)
        for index in range(count):
            (parts / (str(index).zfill(5) + ".part")).unlink()
        parts.rmdir()
        print("Verified source artifact: " + str(total) + " bytes", flush=True)
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
