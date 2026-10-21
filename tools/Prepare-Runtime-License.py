"""Retain Microsoft's runtime terms and include them in the offline setup license.

The original publisher document is preserved. LibreOffice renders a readable
UTF-8 copy; every source paragraph must survive before setup can use it.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
URL = "https://visualstudio.microsoft.com/wp-content/uploads/2025/10/Visual-C-V14-License-Redistributable_and_Runtime_ENU.docx"
SHA256 = "08651651a7602fc7c0e2763de0fde1ff9f868df2780597cd1775ee9d6441c783"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, default=ROOT / "artifacts/payload")
    args = parser.parse_args()
    payload = args.payload.resolve()
    spec = importlib.util.spec_from_file_location("collector", Path(__file__).with_name("Collect-Release-Sources.py"))
    collector = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(collector)
    collector.OUTPUT = ROOT / ".engines/compliance"
    original = collector.OUTPUT / "Microsoft-VC14-license.docx"
    collector.download(URL, original, SHA256)
    runtime = json.loads((payload / "engine/runtime.json").read_text(encoding="utf-8-sig"))
    office = (payload / "engine" / runtime["Office"]).resolve()
    if not office.is_relative_to(payload):
        raise ValueError("Use the actual bundled LibreOffice to prepare setup terms")
    with tempfile.TemporaryDirectory(prefix="CitrusRuntimeTerms-") as temporary:
        folder = Path(temporary)
        result = subprocess.run([str(office), "-env:UserInstallation=" + (folder / "profile").as_uri(),
            "--headless", "--nologo", "--nodefault", "--nofirststartwizard", "--convert-to", "txt:Text (encoded):UTF8",
            "--outdir", str(folder), str(original)], capture_output=True, timeout=120,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        converted = folder / (original.stem + ".txt")
        if result.returncode or not converted.is_file():
            raise RuntimeError("Could not render the Microsoft runtime terms")
        text = converted.read_text(encoding="utf-8-sig")
    compact = "".join(text.split())
    with zipfile.ZipFile(original) as archive:
        document = ET.fromstring(archive.read("word/document.xml"))
    namespaces = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    paragraphs = [
        "".join(node.text or "" for node in p.findall(".//w:t", namespaces))
        for p in document.findall(".//w:p", namespaces)]
    for paragraph in paragraphs:
        if "".join(paragraph.split()) not in compact:
            raise ValueError("A publisher license paragraph changed during text rendering")
    if "Cpp_v14_ENU.1033" not in text:
        raise ValueError("Unexpected Microsoft runtime license document")
    target = payload / "licenses/microsoft"
    target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(original, target / original.name)
    (target / "Microsoft-VC14-license.txt").write_text(text, encoding="utf-8-sig")
    header = ("CITRUS AND SEPARATELY LICENSED MICROSOFT RUNTIME COMPONENTS\n\n"
              "Part 1 applies to Citrus. Part 2 applies only to Microsoft's runtime binaries.\n"
              "Microsoft's terms do not replace or restrict the AGPL rights in Citrus's own code.\n"
              "Other conversion-engine licenses and notices are retained in the installation.\n\n"
              "PART 1 - CITRUS: GNU AFFERO GENERAL PUBLIC LICENSE VERSION 3\n\n")
    composite = header + (ROOT / "LICENSE").read_text(encoding="utf-8") + "\n\nPART 2 - MICROSOFT RUNTIME COMPONENTS ONLY\n\n" + text
    (payload / "INSTALLER_LICENSE.txt").write_text(composite, encoding="utf-8-sig")
    (target / "provenance.json").write_text(json.dumps({"publisher_document": URL, "publisher_document_sha256": SHA256,
        "readable_text_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
        "all_source_paragraphs_retained": True}, indent=2), encoding="utf-8")
    print("Prepared full offline runtime terms; all publisher paragraphs retained.")


if __name__ == "__main__":
    main()
