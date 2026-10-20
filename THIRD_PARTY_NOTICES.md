# Local conversion-engine dependencies

Citrus uses Windows/.NET APIs and can launch separately installed conversion engines. Downloaded engine binaries and Python packages are excluded from Git. The source repository does not bundle those third-party distributions.

| Dependency | Upstream license / information |
|---|---|
| Pillow | MIT-CMU; https://pillow.readthedocs.io/en/stable/about.html |
| pillow-heif | BSD-3-Clause wrapper; the 1.8.1 wheel additionally declares copyleft terms for bundled native libraries; https://github.com/bigcat88/pillow_heif/blob/v1.8.1/LICENSES_bundled.txt |
| imageio-ffmpeg | BSD-2-Clause wrapper; bundled FFmpeg has separate terms; https://github.com/imageio/imageio-ffmpeg |
| FFmpeg | Build-dependent LGPL/GPL; https://ffmpeg.org/legal.html |
| PyMuPDF / MuPDF | GNU AGPL 3.0 or Artifex commercial license; https://pymupdf.readthedocs.io/en/latest/about.html |
| python-docx | MIT; https://github.com/python-openxml/python-docx |
| python-pptx | MIT; https://github.com/scanny/python-pptx |
| openpyxl | MIT; https://openpyxl.readthedocs.io/ |
| Beautiful Soup | MIT; https://www.crummy.com/software/BeautifulSoup/ |
| LibreOffice | MPL 2.0 and component licenses; https://www.libreoffice.org/about-us/licenses/ |

The locally installed imageio-ffmpeg wheel currently supplies FFmpeg 7.1 from Gyan with GPL/version3, libx264 and other optional components enabled. It must not be described as an LGPL-only build. Installed packages retain their license files under the environment's distribution metadata; LibreOffice includes its own notices.

The setup builder now bundles isolated Python, its packages (including their distribution metadata and license files), FFmpeg, and a complete LibreOffice tree including its notices. Python's license remains in the embedded distribution; the Microsoft Visual C++ prerequisite retains Microsoft's terms and signature. Citrus source is AGPL-3.0; that does not replace upstream licenses.

The generated setup remains a local release candidate. Before distributing an engine-inclusive release publicly, inventory the actual binary builds and transitive components, and supply the notices, corresponding source/build information and other materials required by their upstream terms. See RELEASE.md for the source-compliance and signing gates. No complete third-party corresponding-source bundle has been prepared yet.

Source collection and outstanding component-level review are documented in [docs/SOURCE_REVIEW.md](docs/SOURCE_REVIEW.md). `tools/Collect-Release-Sources.py` gathers exact-version Python source distributions, bundled notices and selected native sources into ignored release-review artifacts, with a digest manifest. Collecting those archives is not a compliance certification or permission to distribute the current installer.
