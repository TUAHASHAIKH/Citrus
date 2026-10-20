# Citrus — offline image converter

Citrus runs in the Windows tray. Drag an image, tap Shift, and drop onto a format in the wheel beside your cursor. Originals stay untouched; outputs are saved next to them without overwriting existing files.

## Try it

Use the **Try Citrus** folder. It contains the app, Sample.jpg, your previous conversion results, and START HERE.txt.

- Drag a JPG in Explorer or on the Desktop, then tap **Shift**. Holding Shift before dragging also works.
- Release Shift while keeping the mouse held. The wheel shows **PNG**, **BMP**, and **TIFF** for a JPG.
- Move onto a named wedge and release the mouse. The hovered wedge turns orange.
- Drag a PNG to get **JPG**, **BMP**, and **TIFF** choices instead.
- Escape, the center, and the gaps between wedges are neutral. Releasing the activation key keeps the wheel open.
- The wheel stays beside the original cursor location while selecting and fits within the current monitor's work area.

For the keyboard-accessible picker, double-click Citrus.exe, click **Choose images…**, select files, choose **Output format**, then click **Convert**. Dropping images onto the main window loads them for this workflow.

## Supported images

| Inputs | Outputs |
|---|---|
| JPG / JPEG | PNG, BMP, TIFF |
| PNG | JPG, BMP, TIFF |
| BMP | PNG, JPG, TIFF |
| Single-page TIF / TIFF | PNG, JPG, BMP |

Mixed batches offer targets available to every selected image. JPG + PNG can become BMP or TIFF. All four source formats together leave no common different format; split that batch. Same-format re-encoding is not offered.

PNG/TIFF exports preserve supported alpha transparency. JPG/BMP flatten transparency onto white. JPEG quality is configurable. EXIF orientation is applied; metadata is not copied. This is an 8-bit raster pipeline; archival color-profile fidelity and high-bit-depth preservation are not promised. JPEG's existing compression loss cannot be undone.

Multi-page TIFFs are rejected instead of losing pages. Images above 80 megapixels are rejected after decoder initialization. WebP, HEIC, AVIF, GIF, PDF, audio and video are not supported in this build.

## Tray and settings

Closing or minimizing keeps Citrus active. Double-click its orange-and-leaf tray icon to reopen it. Right-click for:

- **Drag-to-convert enabled** — pause/resume; this preference is saved.
- **Settings…** — Shift/Alt/Control trigger, Dark/Light/System appearance, JPEG quality and reduced animation.
- **Start with Windows** — start hidden at sign-in.
- **Exit Citrus** — stop the app, cancelling an active batch first.

Settings are saved atomically in %LOCALAPPDATA%\Citrus\settings.json. Corrupt settings are preserved with a .corrupt-… suffix and defaults are restored in memory. Startup uses the current user's Windows Run key. Keep the executable at its registered location.

Successful background conversions show a tray notification. Errors open details. Windows notification preferences may suppress balloons. Batches run sequentially on a background thread; another drag is rejected while a batch is active. Cancellation is checked between decode/encode/publication steps; Windows codecs cannot be interrupted midway through an operation.

## Build and developer checks

No SDK, NuGet package, external converter, or network connection is required. The prototype uses C#, Windows Forms, and Windows' .NET Framework compiler/codecs.

~~~powershell
.\build.ps1
& '.\Try Citrus\Citrus.exe'
~~~

Exit from the tray before rebuilding into the same folder. To build and test in a temporary directory:

~~~powershell
.\tools\Test.ps1
~~~

This keeps manual-testing folders clean. Checks cover all 12 routes, transparency, JPEG quality, white backgrounds, multi-page rejection, originals, collisions, corruption, cancellation, route intersection, wedge geometry, settings recovery, layout and background lifecycle. Reports and UI previews stay in the printed temporary directory.

## Source and next work

- src/Citrus.cs: app lifetime, tray, window, input observer, overlay and original regression checks.
- src/ImageConversions.cs: supported routes, conversion and output validation.
- src/RingGeometry.cs: shared wedge drawing and hit testing.
- src/AppSettings.cs: preferences, atomic persistence, palette and settings dialog.
- src/Brand.cs, assets/, tools/GenerateIcon.cs: original vector branding and embedded icon.
- src/PhaseTwoTests.cs: expanded image/settings/interaction checks.

The input observer samples every 25 ms and runs while the UI is hidden. Very fast taps, elevated/non-OLE sources, virtual attachments and some applications can behave differently. Automated tests do not replace manual Explorer, high-contrast and mixed-DPI checks.

The decoder still runs in the app process. Isolated workers, a persistent queue, media engines, installation/signing and editing belong to subsequent milestones. See WINDOWS_IMPLEMENTATION_ROADMAP.md for implementation status.
