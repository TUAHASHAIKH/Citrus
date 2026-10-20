# Third-party source review

The engine-inclusive installer is **not yet cleared for public distribution**. This document records specific remaining work, rather than treating the top-level package license as covering every bundled binary.

## Collected materials

Run `python tools/Collect-Release-Sources.py` after preparing the self-contained payload. It copies bundled Python-package notices and downloads exact-version PyPI source distributions, checking their SHA-256 against PyPI metadata. It also gathers selected upstream native source archives. The generated `artifacts/source-review/source-review.json` records successful downloads, hashes, failures and outstanding review items. Downloads are never executed or extracted.

This collection is useful release evidence; it is not a complete corresponding-source package or a legal compliance sign-off. It must not automatically turn the public-release status green.

The completed local collection contains 13 exact-version Python source archives, 5 native source archives (CPython, base FFmpeg, libheif, libde265 and x265), and 14 copied package notice files. [The checked-in manifest](validation/source-collection.json) records URLs and SHA-256 hashes; archives themselves remain in ignored artifacts. PyPI archives were checked against upstream metadata. Native archives have recorded download hashes, which do not independently prove correspondence to the distributed binary builds.

## Outstanding review

| Distributed component | Required follow-up |
|---|---|
| Gyan FFmpeg 7.1 essentials binary in imageio-ffmpeg | Obtain and verify exact revisions, patches and build materials for its enabled third-party libraries. Merely downloading ffmpeg-7.1 source does not cover the entire static binary. If provenance cannot be established, replace it with a build whose full materials we can supply and rerun codec tests. |
| PyMuPDF/MuPDF | Match the wheel's native MuPDF version, patches, dependencies and build instructions; the Python wrapper source alone is insufficient. |
| pillow-heif wheel | Review the bundled libheif, libde265, x265 and MinGW runtime terms and build materials. The installed wheel's LICENSES_bundled.txt describes additional copyleft terms beyond the BSD wrapper. Resolve compatibility based on actual license texts and distribution boundaries. |
| LibreOffice 26.8.1.1 copied installation | Match exact source release, external sources and build materials; preserve its complete notices. |
| Pillow/lxml and other wheels | Review embedded native libraries and their bundled notices, rather than relying only on package classifiers. |
| Embedded Python and Microsoft VC++ prerequisite | Preserve notices and verify the applicable redistribution terms for the actual distributions. |

## References

- FFmpeg legal information: https://ffmpeg.org/legal.html
- GNU source-distribution FAQ: https://www.gnu.org/licenses/gpl-faq.html
- PyMuPDF licensing: https://pymupdf.io/licensing
- pillow-heif 1.8.1 bundled license information: https://github.com/bigcat88/pillow_heif/blob/v1.8.1/LICENSES_bundled.txt
- LibreOffice licenses: https://www.libreoffice.org/licenses/

Once verified, publish the matching Citrus source, required third-party materials, notices and clear source-access instructions alongside each installer release. Keep versioned records for the exact binaries distributed. A private beta also distributes binaries; inviting testers does not remove upstream license obligations.
