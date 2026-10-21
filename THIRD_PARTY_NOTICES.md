# Local conversion-engine dependencies

Citrus uses Windows/.NET APIs and can launch separately installed conversion engines. Downloaded engine binaries and Python packages are excluded from Git. The source repository does not bundle those third-party distributions.

| Dependency | Upstream license / information |
|---|---|
| Pillow | MIT-CMU; https://pillow.readthedocs.io/en/stable/about.html |
| pillow-heif | BSD-3-Clause wrapper; the 1.8.1 wheel additionally declares copyleft terms for bundled native libraries; https://github.com/bigcat88/pillow_heif/blob/v1.8.1/LICENSES_bundled.txt |
| imageio-ffmpeg | BSD-2-Clause wrapper; bundled FFmpeg has separate terms; https://github.com/imageio/imageio-ffmpeg |
| FFmpeg | Build-dependent LGPL/GPL; https://ffmpeg.org/legal.html |
| PyMuPDF / MuPDF | GNU AGPL 3.0 or Artifex commercial license; https://pymupdf.readthedocs.io/en/latest/about.html |
| lxml | BSD wrapper; bundled iconv 1.17.1 is LGPL-2.1, with separately licensed libxml2, libxslt and zlib. Full native sources and build scripts accompany the source release. |
| Inno Setup 6.7.3 | Copyright 1997-2026 Jordan Russell; portions copyright 2000-2026 Martijn Laan. Full publisher license is installed under licenses/installer. https://jrsoftware.org/files/is/license.txt |
| python-docx | MIT; https://github.com/python-openxml/python-docx |
| pdf2docx 0.5.13 | MIT wrapper; uses separately licensed native dependencies, including PyMuPDF; https://github.com/ArtifexSoftware/pdf2docx |
| fonttools / NumPy / OpenCV headless / Fire / termcolor | Retain their bundled licenses and native-library notices; package metadata is included with the runtime |
| python-pptx | MIT; https://github.com/scanny/python-pptx |
| openpyxl | MIT; https://openpyxl.readthedocs.io/ |
| Beautiful Soup | MIT; https://www.crummy.com/software/BeautifulSoup/ |
| LibreOffice | MPL 2.0 and component licenses; https://www.libreoffice.org/about-us/licenses/ |

The development imageio-ffmpeg wheel supplies FFmpeg 7.1 from Gyan with GPL/version3 and optional libraries enabled. The 0.3.2 installer payload instead uses the pinned BtbN FFmpeg 8.1 GPL/version3 build in `engines/ffmpeg-release.json`; it does not include imageio-ffmpeg or OpenCV's optional FFmpeg video DLL. Both executables must not be described as LGPL-only builds. Installed Python packages retain their full license files in distribution metadata; LibreOffice includes its own notices.

This software is based in part on the work of the FreeType Team. Pillow's bundled license file retains FreeType's notices alongside the notices for its other native libraries.

The setup builder now bundles isolated Python, its packages (including their distribution metadata and license files), FFmpeg, and a complete LibreOffice tree including its notices. Python's license remains in the embedded distribution; the Microsoft Visual C++ prerequisite retains Microsoft's terms and signature. Citrus source is AGPL-3.0; that does not replace upstream licenses.

The generated setup remains a local release candidate pending clean Windows acceptance. Release builds use Visual Studio Community under its accepted license for Microsoft runtime redistribution eligibility. Microsoft runtime binaries remain under Microsoft's terms, separately from Citrus's AGPL license. Setup displays the complete runtime terms; the original publisher document and rendered text are retained under licenses/microsoft. See README.md for release status.

`tools/Collect-Release-Sources.py` gathers exact-version Python source distributions, bundled notices and selected native sources into ignored release-review artifacts, with a digest manifest. Collecting those archives is not a compliance certification or permission to distribute the current installer.

Corresponding sources, dependencies, patches and build instructions for 0.3.2 are packaged at https://github.com/TUAHASHAIKH/Citrus/releases/tag/citrus-sources-0.3.2. Confirm every listed asset is published before distributing setup. Installed SOURCE_INFORMATION.txt identifies the release; full native notices are indexed in licenses/index.json. Components retain their respective licenses.
