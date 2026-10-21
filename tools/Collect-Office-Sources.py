"""Collect LibreOffice release sources and all download.lst external archives.

This preserves the upstream patches and build configuration in the core archive.
It does not assert that a source tag matches a particular installed binary.
"""
import argparse
import concurrent.futures
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import urllib.request

spec = importlib.util.spec_from_file_location("source_collector", Path(__file__).with_name("Collect-Release-Sources.py"))
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


def external_sources(text):
    values = dict(re.findall(r"^([A-Z0-9_]+)\s*:=\s*(.*?)\s*$", text, re.MULTILINE))
    def expand(value):
        for _ in range(20):
            result = re.sub(r"\$\(([A-Z0-9_]+)\)", lambda match: values[match[1]], value)
            if result == value:
                if "$" in result:
                    raise ValueError("Unresolved download.lst expression: " + result)
                return result
            value = result
        raise ValueError("Recursive download.lst expression")
    result = []
    for key, value in values.items():
        if not key.endswith("_TARBALL"):
            continue
        name = expand(value)
        digest = values.get(key.removesuffix("_TARBALL") + "_SHA256SUM", "")
        if not re.fullmatch(r"[a-f0-9]{64}", digest) or Path(name).name != name:
            raise ValueError("Invalid external source mapping: " + key)
        result.append((key.removesuffix("_TARBALL"), name, digest))
    if len(result) < 50:
        raise ValueError("Unexpectedly incomplete LibreOffice download list")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True, help="Build ID from program/version.ini")
    parser.add_argument("--output", type=Path, default=collector.ROOT / "artifacts/source-review/office")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", args.version) or not re.fullmatch(r"[a-f0-9]{40}", args.commit):
        parser.error("Expected a four-part release version and full source commit")
    collector.OUTPUT = args.output.resolve()
    collector.OUTPUT.mkdir(parents=True, exist_ok=True)
    prefix = "https://raw.githubusercontent.com/LibreOffice/core/" + args.commit + "/"
    with urllib.request.urlopen(prefix + "download.lst", timeout=60) as response:
        text = response.read().decode("utf-8")
    (collector.OUTPUT / "download.lst").write_text(text, encoding="utf-8")
    sources = []
    base = "https://download.documentfoundation.org/libreoffice/src/" + args.version.rsplit(".", 1)[0] + "/"
    for component in ("", "-dictionaries", "-help", "-translations"):
        name = "libreoffice" + component + "-" + args.version + ".tar.xz"
        with urllib.request.urlopen(base + name + ".sha256", timeout=60) as response:
            digest = response.read().decode().split()[0]
        if not re.fullmatch(r"[a-f0-9]{64}", digest):
            raise ValueError("Invalid official source digest: " + name)
        sources.append(("release" + component, base + name, name, digest))
    for component, name, digest in external_sources(text):
        sources.append((component, "https://dev-www.libreoffice.org/src/" + name, "external/" + name, digest))
    results = []
    def collect(item):
        component, url, filename, digest = item
        try:
            record = dict(component=component, status="verified", **collector.download(url, collector.OUTPUT / filename, digest))
        except Exception as error:
            record = dict(component=component, status="blocked", url=url, error=str(error))
        print(component + ": " + record["status"], flush=True)
        return record
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        futures = [pool.submit(collect, item) for item in sources]
        for future in concurrent.futures.as_completed(futures):
            result = future.result()
            results.append(result)
            # Keep a partial, truthful inventory if another transfer fails or is interrupted.
            (collector.OUTPUT / "manifest.json").write_text(json.dumps({"version": args.version, "source_commit": args.commit,
                "complete": len(results) == len(sources) and all(item["status"] == "verified" for item in results),
                "archives": results}, indent=2), encoding="utf-8")
    return int(any(item["status"] != "verified" for item in results))


if __name__ == "__main__":
    raise SystemExit(main())
