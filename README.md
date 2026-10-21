# Citrus

An offline Windows converter with a glass-style interface and a format wheel beside your cursor.

<img src="assets/citrus.png" alt="Citrus logo" width="96">

**Open-source development preview.** Source is available here; the full installer has not been published. Local checks have passed, but clean-Windows acceptance and third-party source review remain open.

Licensed under **AGPL-3.0**; see [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).

## Use it

Start Citrus, then drag a file, tap **Shift**, release Shift while keeping the mouse held, and drop onto a named wedge. The hovered choice turns orange. Hold over **More →** for about 0.65 seconds to cycle to the next page; move away before using More again. The center and gaps are neutral. Escape cancels.

Double-click **Try Citrus/Citrus.exe** for the file picker: **Choose files… → Format / action → Convert**. Closing the window keeps Citrus in the tray. Right-click the orange-and-leaf tray icon for settings, engine status, cancel-all, output-folder access, Windows startup, and Exit.

## Conversions and tools

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

The runtime catalog hides profiles whose required local engines/encoders are unavailable. Mixed batches use the intersection of supported routes. Aggregate operations such as PDF merging require multiple files; they save one combined output beside the first selected item. File actions work for folders and unsupported formats too.

## Fidelity and limits

- Originals are preserved. Output names gain numeric suffixes instead of overwriting existing files. Explicit clipboard/terminal actions do not create converted files.
- PNG/TIFF support alpha. JPG/BMP place transparency on white. Metadata is not copied; archival color/high-bit-depth fidelity is not promised. Multi-frame image routes reject animation or multiple pages rather than discarding frames; use a media route for animated GIFs.
- PDF → Word reconstructs editable text with font/size/style, paragraph spacing, tables, images and page sections using pdf2docx. This is a layout reconstruction: complex PDFs, unavailable fonts and different Word renderers can change appearance or pagination. Scan-only image pages are rejected with an OCR message instead of silently disappearing; OCR is not installed. PDF → PPTX produces image slides. PDF → XLSX extracts detected tables, not arbitrary page layout.
- Routes labelled **PDF text** and text/ebook conversions flatten structure and styling. Use the LibreOffice PDF route for Office layout rendering. Fonts and pagination can still differ from Microsoft Office.
- CSV/TSV export uses one worksheet and loses formatting. The simple XLSX adapter reads cached formula values; LibreOffice routes can recalculate.
- PDF compression may not shrink every file. Multi-page exports produce a new folder.
- RAR, Apple Pages, AirDrop, interactive crop/annotation/redaction, OCR and full Tangerine parity remain unfinished. No universal “every format to every format” claim is made.

## Background operation and jobs

The window and wheel use rounded controls, translucent tints, gradients and fine highlights. Supported Windows 11 systems also request the native transient backdrop and extended glass frame; appearance depends on Windows composition/transparency settings. Older systems and high contrast use a readable fallback.

Settings persist in %LOCALAPPDATA%\Citrus\settings.json: Shift/Alt/Control trigger, pause, JPEG quality, Dark/Light/System appearance, reduced animation. Sign-in startup uses the current user's Windows Run key; keep the project and app folders in place.

Conversions run in isolated worker processes. Windows Job Objects terminate their children on cancellation or app exit. The queue holds up to 20 waiting batches, with one active batch and per-file progress. Cancel clears pending work and stops the current worker. Worker staging is beside the destination for non-overwriting atomic publication. The queue is in memory: crash/reboot recovery, detailed history/retry and duration-based media progress remain future work.

Processing uses local engines and does not intentionally upload files. Cloud placeholders or UNC files may require their storage provider/network. Initial engine installation downloads software; conversions do not require that download step again.

## Build and engine setup

The C# shell uses Windows Forms and targets .NET Framework. Builds prefer Visual Studio's compiler and retain the Windows Framework compiler as a development fallback. Native image workers work without Python. The expanded catalog uses Python 3.13, local packages, FFmpeg, and optional LibreOffice. This machine has these engines installed.

~~~powershell
# On another machine, install Python, then provision local dependencies:
.\tools\Install-Engines.ps1 -WithOffice

# Exit Citrus via the tray before overwriting its executable:
.\build.ps1
& '.\Try Citrus\Citrus.exe'
~~~

The build writes engine/worker.py and a machine-local engine/runtime.json next to the executable. Third-party packages remain in the ignored .engines directory or the installed Python environment; this is a development installation, not a standalone redistributable installer. See THIRD_PARTY_NOTICES.md.

## Checks

~~~powershell
.\tools\Test.ps1 -AllEngines
~~~

Builds and fixtures go to temporary directories, keeping Try Citrus clean. Checks cover original image behavior, glass/layout controls, catalog intersection, six-wedge geometry, settings, source preservation, collision handling, Unicode/space-containing paths, worker errors/cleanup, process-tree cancellation, queued background jobs, and real output from every advertised worker profile. Each profile is exercised with a generated representative input, not every possible source/target pair or real-world layout.

Source modules are under src/; engine code and pinned requirements under engines/. Progress and remaining work are recorded in WINDOWS_IMPLEMENTATION_ROADMAP.md.

## Windows installer

The local candidate is `artifacts/setup/Citrus-Setup-0.3.2-x64.exe`. Exit older development copies before installing. Keep sign-in startup selected to launch Citrus into the tray automatically. Settings and converted user files survive uninstall. Windows controls whether the icon appears beside the clock or in the overflow menu. The tray's **Licenses and source** entry opens the installed notices and Citrus license.

The setup targets Windows 10 22H2 (build 19045+) and Windows 11 x64 and bundles Python, conversion libraries, FFmpeg and LibreOffice. Its Microsoft VC++ prerequisite requests elevation if a compatible runtime is missing; Citrus itself runs without elevation. ARM64 is untested.

```powershell
# Build machine: Python 3.13 x64, LibreOffice and Inno Setup 6 required.
.\tools\Build-Setup.ps1
.\tools\Test-Setup.ps1
.\tools\Test-TrayLifecycle.ps1
```

The setup checks cover isolated installation on the developer machine, installed-app regressions, reinstall/uninstall and real tray startup/single-instance/shutdown behavior. Generated output and packaging caches can be rebuilt. `.engines/python` is required by the current manual app; do not remove it without provisioning a replacement runtime.

The 0.3.2 payload replaces the Gyan executable with a pinned BtbN FFmpeg 8.1 build identified in `engines/ffmpeg-release.json`. It excludes the imageio-ffmpeg wheel and OpenCV's unused FFmpeg video plugin; media conversion uses the standalone executable. All 71 advertised profiles pass with this replacement. The development environment still supports imageio-ffmpeg as a fallback.

Matching sources, native dependencies, patches and build instructions are published in the [Citrus 0.3.2 source release](https://github.com/TUAHASHAIKH/Citrus/releases/tag/citrus-sources-0.3.2). The hosting workflow verified every uploaded asset against its SHA256 digest. Source downloads are separate from the fully offline installer. The source release includes integrity manifests, extraction instructions and retained FFmpeg build input because its publisher expires daily builds.

Release builds use the installed Visual Studio Community C# compiler under its accepted license. Microsoft's unmodified signed runtime remains separately licensed; setup displays its complete terms alongside Citrus's AGPL. Rebuilding redistributors must obtain their own applicable redistribution grant. Full native notices are indexed under licenses/index.json, and installed SOURCE_INFORMATION.txt links the matching sources. The source collection, verification, notice collection, packaging and publishing tools are under tools/. Signing is not configured, so Windows may warn about the unsigned installer. Clean Windows testing remains pending.

The 0.3.2 candidate passed all 71 advertised worker profiles, shell checks, tray lifecycle checks, installed license/source checks, installation, installed-app regressions, reinstall and uninstall on the developer machine on 21 October 2026. The source release contains the matching Citrus source commit and payload executable hash. A pinned GitHub workflow handles large source transfers from their original publishers; it does not upload the Citrus installer.

Fresh-machine acceptance remains untested: install without developer tools or preinstalled engines, verify prerequisite acceptance/decline, sign-in startup, Explorer/Desktop Shift-drag at different DPI settings, offline conversions, cancellation, upgrade/uninstall while converting and preservation of originals. Do not label unobserved cases as passed. No account, payments, hosted conversion or automatic updater is implemented.
