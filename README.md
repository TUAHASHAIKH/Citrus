# Citrus

An offline Windows converter with a glass-style interface and a format wheel beside your cursor.

<img src="assets/citrus.png" alt="Citrus logo" width="96">

**Open-source development preview.** Source is available here; the full installer has not been published. Local checks have passed, but clean-Windows acceptance and third-party source review remain open. See [release status](docs/RELEASE_STATUS.md) and [testing guide](docs/WINDOWS_ACCEPTANCE.md).

Licensed under **AGPL-3.0**; see [LICENSE](LICENSE). For the downloadable Windows setup workflow, bundled engines, build/test commands and remaining public-release gates, see [RELEASE.md](RELEASE.md). The installer is currently a local unsigned release candidate.

## Use it

Start Citrus, then drag a file, tap **Shift**, release Shift while keeping the mouse held, and drop onto a named wedge. The hovered choice turns orange. Hold over **More →** for about 0.65 seconds to cycle to the next page; move away before using More again. The center and gaps are neutral. Escape cancels.

Double-click **Try Citrus/Citrus.exe** for the file picker: **Choose files… → Format / action → Convert**. Closing the window keeps Citrus in the tray. Right-click the orange-and-leaf tray icon for settings, engine status, cancel-all, output-folder access, Windows startup, and Exit.

## Conversions and tools

| Family | Supported operations |
|---|---|
| Images | JPG, PNG, BMP, single-page TIFF, WebP, AVIF, HEIC/HEIF; PDF export; combine images into a PDF; resize 50%; compress to JPEG |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA; normalize to WAV |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, GIF; M4V input; audio extraction; mute video |
| PDF | All pages to PNG/JPG at 300 DPI; TXT/Markdown/EPUB text; DOCX paragraphs; PPTX rendered slides; XLSX detected tables; merge, split, compress |
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
- PDF → DOCX reconstructs text paragraphs, not the original layout. PDF → PPTX produces image slides. PDF → XLSX extracts detected tables, not arbitrary page layout. Scans need OCR, which is not installed in this build.
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

The C# shell uses Windows Forms and Windows' .NET Framework compiler. Native image workers work without Python. The expanded catalog uses Python 3.13, local packages, FFmpeg, and optional LibreOffice. This machine has these engines installed.

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
