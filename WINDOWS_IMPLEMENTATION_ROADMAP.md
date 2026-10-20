# Tangerine-style Windows converter: feasibility and implementation roadmap

Research date: 20 October 2026. The original proposal follows the implementation checkpoint below. The workspace was empty at the start of research.

## Current implementation checkpoint

### Installer release candidate

`tools/Build-Setup.ps1` prepares a self-contained x64 Python runtime and all Python engines, FFmpeg and LibreOffice, then produces an Inno Setup installer. Installed configuration uses relative paths and explicit bundled FFmpeg/Office executables. Setup installs per user, offers sign-in startup, launches into the tray, and includes a Microsoft-signed VC++ prerequisite for machines lacking a compatible runtime. The installed app exposes a graceful shutdown signal for upgrades/uninstall, including cancellation of queued/active work. Uninstall preserves user settings and converted files and removes only its own startup registration.

`tools/Test-Setup.ps1` covers isolated install, installed-app regressions with Python absent from PATH, reinstall and uninstall; the local run passed. `tools/Test-TrayLifecycle.ps1` separately passed the real background entry point, duplicate launch and graceful shutdown on an isolated Windows desktop. `tools/Inventory-Engines.py` records actual package versions and binary hashes. Citrus is now AGPL-3.0 following the owner's open-source release decision. See RELEASE.md for the exact installer workflow and remaining public-release gates: signing, corresponding third-party sources/build materials, and clean Windows VM/manual interaction acceptance. No subscription service or billing backend is implemented.

### Expanded converter iteration

The current iteration adds a glass-style shell and ring with rounded controls, highlights, tinted surfaces and Windows native backdrop/frame requests with fallback. A shared runtime catalog now connects built-in image codecs, Pillow/HEIF/AVIF, FFmpeg, PyMuPDF, python-docx/python-pptx, openpyxl, standard-library archives/subtitles and installed LibreOffice.

The catalog covers images, audio/video and audio extraction, PDF page/text/table exports, documents, slides, spreadsheets, ebooks, subtitles and archives. It includes image resize/compress, PDF combine/merge/split/compress, video muting, audio normalization, ZIP creation/extraction and four clipboard/folder/terminal actions. Read README.md for the actual support matrix and fidelity limits; unavailable engines/encoders do not generate active conversion profiles.

All conversions now execute in child workers, including the original native image routes. A bounded in-memory queue accepts up to 20 waiting batches. Job Objects supervise worker descendants and enforce memory limits; cancellation clears queued work and terminates the active process tree. Outputs stage beside the destination and publish without overwriting originals. Multiple-page outputs publish as folders. Aggregate operations save beside the first selected source. Progress is currently per-file, not media-duration based.

The wheel uses six stable slots, with a dwell-operated More wedge for larger catalogs. File metadata/target classification is cached during drag-over. Native background/startup behavior is retained. Engine setup is reproducible through engines/requirements.txt and tools/Install-Engines.ps1; downloaded binaries stay out of Git.

Validation includes generated representative input for every advertised Python worker profile, plus the original image suite, glass/layout checks, Unicode paths, source preservation, PDF page/table semantics, subtitle timing, archive traversal rejection, safe spreadsheet cell imports, pure JSON worker responses, failure cleanup, process-tree termination and queue lifecycle checks. This is not exhaustive validation of every source/target edge, layout, codec variant or Windows drag source.

Remaining: persistent job journal/recovery/history/retry; richer streaming progress; OCR; RAR; native Pages/AirDrop equivalents; interactive crop/annotation/redaction/trim editors; broad manual mixed-DPI/high-contrast/application compatibility; installer signing and engine-distribution compliance work. Developer builds use machine-local runtime configuration; setup builds bundle their runtime. Full Tangerine parity is not declared complete.

### Earlier image-shell checkpoint

The user confirmed the original JPG-to-PNG prototype and drag gesture work on this Windows machine. The next iteration implements the Phase 2 interaction shell and the first image portion of Phase 3:

- Contextual wedges with shared drawing/hit-test geometry; output choices exclude source formats and intersect across mixed batches.
- JPG/JPEG, PNG, BMP and single-page TIFF: 12 directed routes using Windows codecs.
- Sequential batches, per-file errors, cancellation checkpoints, preserved sources, validated output and collision-safe publication.
- Background tray lifetime, close-to-tray, sign-in startup and cursor-relative, DPI-scaled wheel placement.
- Saved pause state, Shift/Alt/Control triggers, JPEG quality, Dark/Light/System appearance and reduced animation; system appearance changes refresh the palette.
- Accessible native file-picker/output controls and settings dialog; high-contrast system colors are respected.
- Automated route, transparency, quality, multi-page, geometry, settings, layout and background-lifecycle checks. Tests build in temporary directories, keeping manual-testing folders clean.

**Actual prototype stack:** C# / .NET Framework / Windows Forms and GDI+, with modular source files and no external packages. This differs from the proposed .NET 10 / WPF architecture because the SDK was unavailable. The working interaction is retained while the product grows; the original project split and engine adapters below remain future architecture targets.

**Validation limits:** the new multi-format shell needs user acceptance and broader Explorer/application, high-contrast and mixed-DPI testing. Phase 2's full compatibility exit gate is not declared passed. Decoding remains in-process on a background thread; there is no isolated worker, persistent queue, FFmpeg, PDF or Office engine. Polling replacement/evaluation remains pending. Multi-page TIFFs are rejected; archival color/high-bit-depth fidelity is outside this build's contract.

**Next milestone:** finish the Phase 2 manual compatibility matrix, then add a bounded job queue, isolated workers and visible progress/retry before audio/video engines and their distribution/license requirements. Existing image routes and tray behavior form the regression baseline.

## 1. Decision and evidence

The core experience is feasible on Windows: start dragging files, tap a key, see an animated radial menu, and drop on a format to convert locally. A production-quality equivalent requires a Windows implementation of the interaction layer rather than a direct macOS port.

Tangerine's official site describes a latched wheel: after tapping the activation key, releasing that key does not close the menu. It advertises conversion, tools and actions wheels, local processing, and a broad format catalog. Its browser demonstration explicitly uses simulated operations; it is useful as an interaction reference, not evidence about its internal conversion engine. [Official product](https://tangerineformac.com/)

No official Tangerine source repository or open-source license was established in this research. Do not assume its implementation or assets are available for reuse. Independently branded, licensed projects can be evaluated as implementation references:

- [Filee](https://github.com/KnifeLemon/Filee) documents a Windows build and a key-triggered donut menu.
- [tangerine-windows](https://github.com/CristianO2601/tangerine-windows) explicitly identifies itself as an independent reimplementation.
- [Clementine](https://github.com/seppedelanghe/Clementine) is an MIT-licensed macOS implementation of a similar interaction.

These repositories are supporting evidence, not independently verified quality benchmarks. Their binaries were not executed and their source was not audited here.

**Recommendation:** Windows 11 x64 first; C#/.NET 10 LTS, WPF, Win32/OLE interop, and out-of-process conversion engines. Add ARM64 after verifying every native dependency. Treat Windows 10 as a separately tested compatibility target if needed. Microsoft lists .NET 10 as supported through November 2028. [Runtime policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

## 2. What “exact replication” should mean

Target behavioral and visual similarity: radial geometry, animation, contextual options, hover feedback, drop-to-convert, local output, progress, cancellation and batch processing.

Do not promise identical behavior in every source application, identical pixels before observing the actual desktop app, macOS-only integrations, or lossless conversion between fundamentally different formats.

Before implementation, record the reference desktop experience where legitimately available: wheel dimensions, segment counts, opening/closing animation, cursor placement, scrolling/overflow, key latching, cancellation, batch rules and progress placement. Public web behavior is provisional where desktop behavior is unknown. Use independent product branding and original assets unless reuse permission is established.

### Proposed interaction contract

1. Run as a per-user tray application.
2. Detect a possible drag and a fresh activation-key press.
3. Show a small nonactivating drop surface near/under the pointer.
4. Confirm that the incoming OLE payload contains supported file data.
5. Display compatible conversions; latch the wheel while dragging.
6. Highlight the wedge under the cursor; accept only a valid drop.
7. Queue conversion, close the wheel and show lightweight progress.
8. Write a new file beside the source or to the configured directory.
9. Never modify or delete the source as a conversion side effect.

Suggested bindings: Shift for conversion, Shift+Alt for tools, and a configurable actions gesture. Windows uses Shift/Ctrl/Alt in normal drag semantics, so every binding requires conflict testing. An ordinary drag without activation must behave normally. Provide pause, app exclusions, remapping and a tray-based fallback.

## 3. Architecture

```text
Tray / settings / accessible file picker
                   |
Input observer -> Drag coordinator -> OLE drop target
                                         |
                                Contextual radial overlay
                                         |
                                  Conversion request
                                         |
Capability registry -> Job planner -> Bounded queue
                                         |
                             Worker process supervisor
                              /        |          \
                         Images     FFmpeg      PDF / Office / archives
                                         |
                         Output validation and atomic publication
                                         |
                              Progress / reveal / retry
```

### Projects and responsibilities

| Project | Responsibilities |
|---|---|
| `Converter.App` | WPF tray host, wheel, settings, progress, accessibility |
| `Converter.Windows` | Input observation, HWND lifetime, OLE/COM, monitors, shell actions |
| `Converter.Core` | Capabilities, requests, job state, output policy; no UI dependencies |
| `Converter.Worker` | Worker host and native engine adapters |
| `Converter.Tests` | Capability, queue, output-safety and conversion fixture tests |
| `Converter.IntegrationTests` | Windows drag scenarios and packaged-build checks |
| `engines/` | Versioned engines, manifests, notices and build provenance |

Use WPF retained vector drawing for the first wheel implementation. WPF supports graphics, animation and Windows interop; actual transparent-window performance must be measured. If it misses frame targets, move only overlay rendering to a native Direct2D/DirectComposition host. [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)

Use a current-user-only named pipe for UI/worker communication, framed versioned JSON messages, validated request IDs, and bounded message sizes. Expose no network listener. Start with an in-process queue plus persistent job journal; a separate Windows service is unnecessary.

## 4. The highest-risk component: activating during another app's drag

### Do not confuse input observation with obtaining the file

Mouse-down, movement and Shift indicate only a candidate gesture. They do not prove a file drag, nor reveal the dragged files. Explorer selection, the clipboard, screenshots and guessed window titles are not reliable drag payload sources.

The authoritative payload comes from `IDataObject` when our registered OLE target receives `DragEnter`/`Drop`. Windows documents `RegisterDragDrop`, message pumping, and `OleInitialize` requirements. [OLE registration](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop)

### Proposed implementation

- Observe mouse/button/key events with a lightweight input component. Compare Raw Input against `WH_MOUSE_LL`/`WH_KEYBOARD_LL` during the prototype.
- If using hooks, keep them on a dedicated message-loop thread, enqueue events and return immediately; never probe files, render or invoke converters inside callbacks. Microsoft warns that timed-out keyboard hooks can be silently removed. [Hook behavior](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- Track mouse movement beyond the system drag threshold and a new trigger-key transition.
- Show a topmost, no-activation HWND with a deliberately hit-testable small capture area. Do not capture the mouse, steal focus, inject a new drag, or create a desktop-sized invisible input blocker.
- Initialize the owned OLE STA correctly and register the target once per HWND. Do not double-register an HWND already managed as a WPF drop target.
- On `DragEnter`, accept supported file representations and populate the wheel. On unsupported objects, dismiss the candidate surface promptly.
- Keep the wheel center stable while selecting; clamp it to the current monitor work area.
- Handle `DragOver`, `DragLeave` and `Drop`; revoke registration on destruction.
- `Drop` is the commit event. Mouse-up alone must never start a job.
- Return `DROPEFFECT_COPY` only when the source permits copying and the drop is accepted. Never return MOVE for a conversion; reject incompatible allowed-effects combinations.

An appearing window beneath the pointer, transparent areas and OLE target routing are **prototype questions**, not settled assumptions. Check whether minimal subsequent pointer movement is required. Try a native drop HWND with WPF rendering separated from hit testing if a transparent WPF window is unreliable.

### State machine

```text
Idle -> CandidateGesture -> AwaitingOlePayload -> WheelOpen
WheelOpen -> AcceptedDrop -> Queueing -> Idle
CandidateGesture / AwaitingOlePayload / WheelOpen -> Cancelled -> Idle
```

Escape, invalid data, source cancellation, mouse release without accepted Drop, session lock and timeout must clean up transient UI. DragLeave into the outside area should withdraw the overlay and allow normal targeting; test event ordering carefully. Releasing the activation key alone must not close a latched wheel.

### Payload scope

MVP: physical paths via `CF_HDROP`. Enumerate Unicode paths safely, validate existence after drop, and handle multi-file input.

Later: virtual attachments via `FILEDESCRIPTOR`/`FILECONTENTS`, including stream materialization, bounded storage and proper COM lifetime. Do not pass raw apartment-bound objects into arbitrary worker threads. Use documented asynchronous data-object support where available; otherwise reject unsupported virtual sources cleanly until their transfer lifecycle is implemented. [Shell transfer scenarios](https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios)

Run without elevation. Elevated source apps, secure desktops, remote sessions and non-OLE sources are separate compatibility cases, not universal guarantees.

## 5. Wheel UI and accessibility

Initial design values, to be tuned against reference observation:

- Diameter approximately 300–360 device-independent pixels.
- Six to eight visible wedges, with paging or rotation for overflow.
- Opening animation 120–180 ms; hover transition 60–100 ms.
- A central neutral area that does not trigger conversion.
- Fixed drop geometry during an active selection; animate decoration without moving the destination under the user.
- Polar hit testing using `atan2`, inner/outer radii and normalized angles.
- Per-monitor DPI awareness; correct physical-pixel to DIP conversion and negative monitor coordinates.
- Stable placement at monitor edges and when taskbars change the work area.
- System/light/dark themes, high contrast, reduced motion, readable labels.

The visual HWND's transparency must not make its active wedges click-through. Conversely, transparent space around the wheel must not accidentally block normal targets. Verify the chosen window-region and OLE hit-testing strategy experimentally.

Provide a conventional, keyboard-accessible picker from the tray with UI Automation names and focus management. Do not force keyboard focus onto the wheel during an external drag. Add wheel-scroll behavior only after confirming source compatibility; drag-loop wheel delivery is a separate test.

## 6. Conversion capabilities and engine choices

Define support as tested **directed conversion routes**, not a marketing count of extensions.

| Family | Proposed engine | Initial routes | Constraints / expansion |
|---|---|---|---|
| Images | ImageMagick/Magick.NET in worker | JPEG, PNG, WebP, BMP, TIFF | AVIF/HEIC only after bundled codec verification; SVG input with external resources disabled |
| Audio/video | FFmpeg + ffprobe | MP4, WebM, MKV; MP3, WAV, FLAC, AAC/M4A, Opus | Explicit codec/container profiles; available encoders vary by build |
| PDF render/text | PDFium adapter | PDF to PNG/JPEG; text-layer extraction | OCR is separate; rendering is not editable document reconstruction |
| PDF structure | qpdf | Merge, split, reorder | Not an Office converter or rasterizer |
| Office | Optional LibreOffice engine pack | DOCX/XLSX/PPTX/ODT/ODS to supported exports including PDF | Font/layout variation; isolated profile and restricted document execution |
| Subtitles | Managed parser | SRT ↔ VTT; transcript TXT | Disclose style/position information loss |
| Archives | libarchive adapter | ZIP/TAR/TAR.GZ create/extract/repack | Verify read/write matrix; RAR input only where supported |
| OCR | Optional Tesseract pack | Scanned PDF/images to text | Language packs, confidence and layout limitations |

Engine references: [FFmpeg](https://ffmpeg.org/ffmpeg.html), [ImageMagick policy](https://imagemagick.org/security-policy/), [PDFium](https://pdfium.googlesource.com/pdfium/), [qpdf](https://qpdf.readthedocs.io/en/stable/overview.html), [LibreOffice command line](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html), [libarchive](https://www.libarchive.org/), [Tesseract](https://github.com/tesseract-ocr/tesseract).

### Capability model

Each route specifies:

```text
id, sourceFamily, sourceConstraints, targetExtension,
engineId, engineVersionConstraint, encoderProfile,
defaultOptions, optionSchema, batchMode,
lossWarnings, estimatedResourceClass, validationStrategy
```

Discover installed engine versions and encoder support at startup, outside the drag path. Cache capability metadata. Use extensions for fast provisional classification and inspect actual content before processing.

For mixed selections, initially offer the intersection of supported targets. Treat aggregation, such as multiple images into one PDF, as a separate operation with explicit ordering. Hide unavailable routes or explain the missing engine; never expose buttons that silently fail.

### Fidelity policies

- JPEG export flattens alpha onto a defined background.
- Apply EXIF orientation correctly; define color-profile, metadata and animation handling.
- Media presets specify codecs, pixel format, quality, audio layout and subtitle handling, not just filename extension.
- Remux only when streams are compatible with the target container.
- Spreadsheet-to-CSV requires a sheet policy and warns about lost formulas/formatting.
- PDF-to-DOCX/XLSX/PPTX requires layout/table reconstruction and its own quality benchmark. Defer it; OCR alone does not solve it.
- Do not promise reliable native `.pages` output or Apple's exact renderer on Windows.

## 7. Worker, queue and output lifecycle

Request fields: job ID, operation ID, source paths or staged input IDs, validated options, output directory policy and collision policy.

Job states: Queued → Probing → Running → Validating → Publishing → Completed, with Failed and Cancelled terminal branches.

- Invoke engines using structured argument lists, `UseShellExecute=false`, known absolute binary paths and hidden windows. No command-shell string interpolation.
- Drain stdout/stderr asynchronously and cap captured log size.
- Use FFmpeg machine-readable progress and probed duration; show indeterminate progress when duration is unknown.
- Bound concurrency initially to one heavy video task and a small image pool; tune from CPU/RAM measurements.
- Supervise process trees with Windows Job Objects; implement graceful cancellation followed by termination.
- Process isolation improves reliability but is not automatically a security sandbox. Introduce restricted tokens or stronger isolation only with explicit compatibility testing.
- Stage outputs in unique per-job locations on the destination volume. Validate before a non-overwriting final rename.
- Default collision handling: `name-converted.ext`, then a unique numeric suffix. Resolve races atomically, not just with an existence check.
- Validate media through probing/decoding samples; images through reopening and dimensions; documents/PDF through format-specific checks. A zero exit code alone is insufficient.
- Preserve partial batch successes and offer per-file retry.
- Recover interrupted jobs on restart without silently re-running destructive actions.

Keep user settings and job metadata beneath `%LOCALAPPDATA%`; use schema-versioned atomic JSON settings and SQLite for the job journal if persistence becomes substantial. Do not retain document content in logs.

Handle read-only folders, missing inputs, disk exhaustion, locked files, long paths, Unicode, UNC paths and disconnected shares. Cloud placeholders may require provider network access to hydrate: strict offline mode should reject unavailable local content with a clear explanation.

## 8. Additional tools and Windows actions

Deliver tools in layers:

1. Image resize/compress, audio/video trim, PDF merge/split.
2. Image crop, metadata removal, video crop/speed/snapshot, audio normalization.
3. Annotation, collage, PDF page organizer, preview-based redaction and advanced media editing.

Editors open only after a successful drop onto the tool. Define batch-compatible operations separately from per-file interactive editing.

Support a genuine destructive PDF redaction implementation or explicitly offer flattened raster output that removes underlying text; drawing black rectangles over PDF text is insufficient.

Windows equivalents:

| Reference action | Windows implementation |
|---|---|
| Copy | Shell file clipboard representation with correct copy semantics |
| Copy Path | Unicode clipboard text |
| Move | Explicit destination and shell file-operation handling; independent of conversion |
| Terminal | Open a configured terminal at a directory using safe argument handling |
| Float | Always-on-top shelf containing file references, drag-out via a fresh data object |
| AirDrop | Separate Windows sharing feature after API feasibility testing; no claim of AirDrop compatibility |

Add Explorer integration after the core app is stable: start with Send To / Open With. Evaluate a modern Explorer command extension only if needed; do not put heavy conversion logic inside Explorer.

## 9. Security, licensing and distribution

Offline processing still parses untrusted data:

- Disable network delegates/external references where possible; configure ImageMagick coder and resource restrictions.
- Limit decompressed archive bytes, entry count, nesting and processing time; reject traversal and unsafe link targets.
- Disable Office macro execution and external-link updates; use a dedicated LibreOffice profile and bounded concurrency.
- Pin engine artifacts and checksums; keep an SBOM and dependency update process.
- Test offline behavior in a network-disabled VM. Updates must be separate from conversion and optional.

Choose the app's license independently from bundled engines. Audit every binary's exact build options and dependencies. FFmpeg's licensing changes when GPL components are enabled; some configurations are not redistributable. Invoking a binary as a subprocess does not erase that binary's distribution obligations. Include notices and corresponding source/build information as required. [FFmpeg licensing](https://www.ffmpeg.org/legal.html)

Package a self-contained x64 build in a per-user signed installer, plus a portable build if practical. Use an established installer tool, register optional startup, and remove registrations on uninstall. Plan signed update manifests, package hashes, interrupted-update recovery and settings migration. Signing is useful but does not guarantee immediate SmartScreen reputation.

Keep the initial media/image pack bundled for genuinely offline first use. Offer a full offline bundle with optional Office/OCR engines; disclose which formats depend on those packs. Measure package size before selecting distribution limits.

## 10. Roadmap, deliverables and gates

Estimates assume one experienced Windows developer working full time, existing engines and access to representative Windows hardware. They are engineering estimates, not guarantees.

| Phase | Effort | Deliverable | Exit gate |
|---|---:|---|---|
| 0. Reference specification and reuse audit | 2–3 days | Behavior matrix, recorded UI observations, route scope, dependency/license inventory | Agreement on MVP and explicit deferred features |
| 1. Native drag feasibility spike | 5–10 days | Tray host, key trigger, OLE overlay, PNG/JPEG proof conversion | Real Explorer drags work; originals safe; cancellation and ordinary dragging preserved |
| 2. Interaction shell | 1–2 weeks | Animated wheel, contextual wedges, settings, DPI handling, keyboard fallback | Multi-monitor and normal-drag regression matrix passes |
| 3. Conversion MVP | 2–3 weeks | Images + core audio/video, queue, progress, cancel, output policy | Every advertised route has passing fixtures and failure handling |
| 4. Release hardening | 1–2 weeks | Installer, offline bundle, logging, recovery, accessibility | Clean-machine and network-disabled acceptance tests pass |
| 5. Broader conversions | 3–5 weeks | PDF, Office pack, subtitles, archives, optional OCR | Each route meets a documented fidelity criterion |
| 6. Advanced tools and actions | 4–8 weeks | Editors, shelf, metadata, richer batches, shell integration | Tool-specific UX and data-preservation tests pass |
| 7. Polish and compatibility expansion | 2–4 weeks | ARM64 where viable, performance tuning, extended app matrix | Release candidate meets measured quality targets |

Expected useful MVP: roughly 6–10 weeks. Broad, polished parity: roughly 4–7 months, with complex PDF reconstruction or extensive editing potentially extending this. A beginner learning COM, WPF and codecs should budget materially longer.

### Phase 1 go/no-go tests

- Drag files from Explorer and Desktop; tap Shift; select a wedge and drop.
- Tap and release Shift while continuing to drag; wheel remains open.
- Escape cancels without producing files.
- Drop outside the wheel reaches the intended original target.
- Unsupported text/browser/image-object drags are not misidentified as filesystem files.
- Shift/Ctrl normal Explorer semantics remain usable without an activated wheel.
- No activation, focus stealing, unintended moves, source deletion or stuck mouse capture.
- Repeat across mixed DPI monitors, edge positions and negative coordinates.
- Verify return to normal state after failed drops and Explorer restart.

If capture is unreliable, time-box a native HWND alternative before building further UI. A permanent drop zone is a usable fallback product, but it does not satisfy exact gesture parity and must be reported as a changed scope.

## 11. Test strategy and performance targets

Unit tests: state machine, polar hit testing, capability intersection, naming/collision rules, option validation and job transitions.

Integration fixtures: transparent/rotated/animated/large images; variable-frame-rate video; multichannel audio; corrupt files; complex Office layout; scanned/encrypted PDFs; traversal and expansion-bomb archives.

Validate content invariants rather than output-byte equality for lossy conversions. Hash originals before/after regression batches. Test cancellation during probing, conversion, validation and publication.

Interactive testing is required: an in-app synthetic drag test does not prove acceptance of Explorer's external OLE drag. Cover supported Windows 11 versions, Explorer/Desktop, multiple monitors, 100/125/150/200% DPI, display changes, RDP, accessibility settings, cloud placeholders and privilege mismatches.

Initial targets to measure on declared reference hardware:

- Idle CPU below 1% with no continuous high-frequency polling.
- Warm trigger-to-visible response below 100 ms at the 95th percentile.
- Near-60 fps animation where supported; reduced-motion alternative.
- No UI freezes while inspecting large files or converting.
- Cancellation acknowledged immediately; worker exits within a documented deadline.
- No writes outside explicitly chosen destinations and owned staging locations.
- No conversion network traffic in offline acceptance testing.

Track per-stage timing and memory locally, with opt-in export of diagnostics. Avoid promising a fixed idle-memory budget until native engine loading and WPF overhead are measured.

## 12. First implementation backlog

1. Pin .NET SDK; create solution, app manifest, CI build and project boundaries.
2. Implement single-instance tray lifetime and clean shutdown.
3. Create native OLE drop target with path logging to a local diagnostic view.
4. Add candidate drag detection and latched trigger state.
5. Render a two-wedge PNG/JPEG wheel with stable hit regions.
6. Implement a worker conversion and safe non-overwriting output publication.
7. Run Phase 1 manual matrix and record evidence.
8. Only after the gate passes, expand animation, formats and editors.

The first milestone must prove the defining gesture and source safety. Format count is not the first success criterion.
