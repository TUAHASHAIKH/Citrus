# Local conversion-engine dependencies

Citrus uses Windows/.NET APIs and can launch separately installed conversion engines. Downloaded engine binaries and Python packages are excluded from Git. The source repository does not bundle those third-party distributions.

| Dependency | Upstream license / information |
|---|---|
| Pillow | MIT-CMU; https://pillow.readthedocs.io/en/stable/about.html |
| pillow-heif | BSD-3-Clause wrapper; libheif and codec dependencies retain their own terms; https://github.com/bigcat88/pillow_heif |
| imageio-ffmpeg | BSD-2-Clause wrapper; bundled FFmpeg has separate terms; https://github.com/imageio/imageio-ffmpeg |
| FFmpeg | Build-dependent LGPL/GPL; https://ffmpeg.org/legal.html |
| PyMuPDF / MuPDF | GNU AGPL 3.0 or Artifex commercial license; https://pymupdf.readthedocs.io/en/latest/about.html |
| python-docx | MIT; https://github.com/python-openxml/python-docx |
| python-pptx | MIT; https://github.com/scanny/python-pptx |
| openpyxl | MIT; https://openpyxl.readthedocs.io/ |
| Beautiful Soup | MIT; https://www.crummy.com/software/BeautifulSoup/ |
| LibreOffice | MPL 2.0 and component licenses; https://www.libreoffice.org/about-us/licenses/ |

The locally installed imageio-ffmpeg wheel currently supplies FFmpeg 7.1 from Gyan with GPL/version3, libx264 and other optional components enabled. It must not be described as an LGPL-only build. Installed packages retain their license files under the environment's distribution metadata; LibreOffice includes its own notices.

This is a development setup using local engines, not a redistribution package. Before distributing an engine-inclusive release, inventory the actual binary builds and transitive components, and supply the notices, corresponding source/build information and other materials required by their upstream terms. The repository does not choose or replace those upstream licenses.
