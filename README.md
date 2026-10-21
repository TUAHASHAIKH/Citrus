# Citrus

<img src="assets/citrus.png" alt="Citrus logo" width="96">

**Drag. Tap Shift. Convert.** An open-source, offline file converter for Windows with a format wheel beside your cursor.

[Downloads & releases](https://github.com/TUAHASHAIKH/Citrus/releases) · [Report a bug](https://github.com/TUAHASHAIKH/Citrus/issues) · [Contribute](#contributing) · [Roadmap](ROADMAP.md)

## Install

Requires **Windows 10 22H2 (19045+) or Windows 11, x64**.

The fully offline **0.3.2 installer is prepared but has not yet been uploaded to Releases**. The existing corresponding-source release contains developer materials, not the Citrus setup file. Once published, the installer will be available from the releases link above.

Run the setup file and leave **Start Citrus when I sign in to Windows** selected if you want automatic startup. Citrus runs in the system tray; Windows may place its icon in the overflow menu. Python, FFmpeg and LibreOffice are bundled; users do not need to install development tools. The Microsoft runtime prerequisite may request administrator approval.

The installer is unsigned. Verify downloads against the release's SHA256SUMS.txt. Installation and conversion work offline.

## Use Citrus

1. Drag a file in Explorer or on the desktop.
2. Tap and release **Shift** while continuing to hold the mouse button.
3. Drop onto a named format in the wheel. **Escape** cancels.

Hold over **More →** for about 0.65 seconds to see additional choices; move away before using More again. The center and gaps are neutral.

Open Citrus from the Start menu for **Choose files → Format / action → Convert**. Closing the window keeps it in the tray. Right-click its tray icon for settings, engine status, cancellation, startup preferences and Exit.

Outputs are saved beside the original files. Existing files are never overwritten.

## Supported conversions

| Family | Supported operations |
|---|---|
| Images | JPG, PNG, BMP, single-page TIFF, WebP, AVIF, HEIC/HEIF; PDF export; combine images into a PDF; resize 50%; compress to JPEG |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA; normalize to WAV |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, GIF; M4V input; audio extraction; mute video |
| PDF | All pages to PNG/JPG at 300 DPI; TXT/Markdown/EPUB text; Word with editable layout/tables/images; PPTX rendered slides; XLSX detected tables; merge, split, compress |
| Documents | DOC/DOCX/ODT/RTF through LibreOffice; TXT/Markdown/HTML/EPUB text conversions and DOCX/PDF text export |
| Slides | PPT/PPTX/ODP to PDF, PPTX/ODP, or all pages as PNG/JPG |
| Spreadsheets | XLS/XLSX/ODS/CSV/TSV to XLSX/ODS/CSV/TSV/PDF where applicable |
| Subtitles | SRT ↔ VTT; plain transcript TXT |
| Archives | ZIP/TAR/TAR.GZ/GZIP/TGZ repacking and extraction; arbitrary files/folders into ZIP |
| File actions | Copy path, copy files, show folder, open Terminal |


Available choices depend on the selected files and installed encoders. Mixed batches show their shared supported routes. Combined outputs are saved beside the first selected file.

## Known limitations

- Originals are preserved. Output names gain numeric suffixes instead of overwriting existing files. Explicit clipboard/terminal actions do not create converted files.
- PNG/TIFF support alpha. JPG/BMP place transparency on white. Metadata is not copied; archival color/high-bit-depth fidelity is not promised. Multi-frame image routes reject animation or multiple pages rather than discarding frames; use a media route for animated GIFs.
- PDF → Word reconstructs editable text with font/size/style, paragraph spacing, tables, images and page sections using pdf2docx. This is a layout reconstruction: complex PDFs, unavailable fonts and different Word renderers can change appearance or pagination. Scan-only image pages are rejected with an OCR message instead of silently disappearing; OCR is not installed. PDF → PPTX produces image slides. PDF → XLSX extracts detected tables, not arbitrary page layout.
- Routes labelled **PDF text** and text/ebook conversions flatten structure and styling. Use the LibreOffice PDF route for Office layout rendering. Fonts and pagination can still differ from Microsoft Office.
- CSV/TSV export uses one worksheet and loses formatting. The simple XLSX adapter reads cached formula values; LibreOffice routes can recalculate.
- PDF compression may not shrink every file. Multi-page exports produce a new folder.
- RAR, Apple Pages, AirDrop, interactive crop/annotation/redaction, OCR and full Tangerine parity remain unfinished. No universal “every format to every format” claim is made.


Settings are stored in `%LOCALAPPDATA%\Citrus\settings.json`. The queue is held in memory and does not recover after a crash or restart. Processing uses local engines; cloud placeholders and network shares can still require their storage provider or network. No accounts, subscriptions or automatic updater are implemented.

## Build from source

Use Windows and PowerShell. The shell is C# / Windows Forms targeting .NET Framework. Builds prefer Visual Studio's C# compiler, with the Windows Framework compiler as a development fallback. Expanded conversions need **Python 3.13 x64** and the local engines.

```powershell
git clone https://github.com/TUAHASHAIKH/Citrus.git
cd Citrus
.\tools\Install-Engines.ps1 -WithOffice
.\build.ps1
& '.\Try Citrus\Citrus.exe'
```

Exit a development copy from its tray menu before rebuilding its executable. `Try Citrus` is a local manual-testing folder; its executable and runtime configuration are generated and excluded from Git. Keep its configured `.engines` runtime in place while using it.

```powershell
# Build into a temporary folder and run regression checks:
.\tools\Test.ps1 -AllEngines
```

| Directory | Purpose |
|---|---|
| `src/` | Windows shell, tray, drag interaction, job control and native regression checks |
| `engines/` | Conversion worker, pinned dependencies and worker tests |
| `assets/` | App icon and README logo |
| `installer/` | Inno Setup installer definition |
| `tools/` | Build, test, engine provisioning and required source/license packaging |
| `Try Citrus/` | Manual-testing instructions and public sample fixture; generated app ignored |

Generated engines, installers, logs, conversion fixtures, source archives and local IDE files are excluded from Git.

## Build the installer

Build-machine requirements: Python 3.13 x64, LibreOffice, Inno Setup 6.7.3, and an applicable Visual Studio license for redistributing Microsoft's runtime. See [third-party notices](THIRD_PARTY_NOTICES.md).

```powershell
.\tools\Build-Setup.ps1
.\tools\Test-Setup.ps1
.\tools\Test-TrayLifecycle.ps1
```

The setup is written to `artifacts/setup/Citrus-Setup-0.3.2-x64.exe`. Source collection and verification must finish before a release build; `-PrepareOnly` stages and tests a development payload without compiling setup. Source/license tools under `tools/` and the pinned source-release workflow retain the corresponding sources, patches, build instructions and integrity manifests.

## Validation

Version 0.3.2 passed all 71 advertised worker profiles, shell/tray checks, installed license/source checks, installation, installed-app regressions, reinstall and uninstall on the developer machine. The maintainer also reports a successful installation and use on a friend's Windows laptop. That external test is user-reported; its Windows version and full interaction matrix were not recorded. Broader mixed-DPI, accessibility and application compatibility testing remains welcome.

## Contributing

Bug reports, conversion fixes, accessibility improvements and documentation contributions are welcome.

1. Open an issue to discuss substantial changes.
2. Fork the repository and create a focused branch.
3. Keep originals safe, preserve tray/startup behavior, and run the relevant checks with `tools/Test.ps1`.
4. Open a pull request explaining the change and how you tested it.

For bug reports, include your Windows version, Citrus version, file type, expected behavior and reproduction steps. Share only files you have permission to publish. Keep engines, installers, generated test output, credentials and personal documents out of commits. Review [ROADMAP.md](ROADMAP.md) for planned work.

## License and corresponding sources

Citrus is licensed under **AGPL-3.0**. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

The [0.3.2 corresponding-source release](https://github.com/TUAHASHAIKH/Citrus/releases/tag/citrus-sources-0.3.2) supplies matching sources, native dependencies, patches and build instructions with verified SHA256 manifests. These developer downloads are separate from the offline installer and are not needed to use Citrus. Installed notices are available through **Licenses and source** in the tray menu. Microsoft runtime components retain their separate proprietary terms.
