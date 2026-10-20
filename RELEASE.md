# Desktop release 0.3.0

This is an offline desktop product with a setup installer. No account, subscription enforcement, payments, hosted conversion service or automatic updater is implemented.

## Installation experience

Run `artifacts/setup/Citrus-Setup-0.3.0-x64.exe`, accept the AGPL license, keep the default sign-in startup option, and finish. Citrus launches into the Windows system tray. Windows may place it in the overflow menu; drag the icon beside the clock to keep it visible. The installer does not override Windows tray preferences.

Drag an actual local file from Explorer/Desktop, tap and release Shift while continuing to hold the mouse, move onto a named orange wedge, and release the mouse. Hold over More to browse additional profiles. Originals remain in place and outputs appear beside them. Close the main window to keep Citrus running; use the tray menu to exit.

Targets: Windows 10 build 19041 or later and Windows 11, x64. ARM64 and other architectures are not release-tested. Python, libraries, FFmpeg, and LibreOffice are bundled; no user-managed Python or Office installation is required. The Microsoft Visual C++ x64 prerequisite is bundled and requests administrator approval only when its runtime is missing/outdated. Conversion itself runs without elevation. The setup needs substantial disk space because it includes the complete Office engine.

Updates use the same application ID and install directory. The installer asks a running installed copy to cancel queued/active work and exit before replacing files. Settings in `%LOCALAPPDATA%/Citrus` and converted user files survive uninstall. Uninstall removes its own startup entry, only if that entry still points to this installation. Close older development prototypes from their tray menu before installing the first packaged version.

## Build and validation

```powershell
winget install JRSoftware.InnoSetup
# Developer machine only: Python 3.13 x64 and LibreOffice must be present for preparing the payload.
powershell -ExecutionPolicy Bypass -File tools/Build-Setup.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-Setup.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-TrayLifecycle.ps1
```

`Build-Setup.ps1` downloads an isolated Python embeddable runtime, installs all engine dependencies into it, copies LibreOffice, verifies the Microsoft prerequisite signature, requires at least 71 worker profiles, runs every profile through the real conversion suite, then compiles setup. `runtime.json` uses paths relative to the installed engine directory. The release artifacts and dependency cache are ignored by Git.

`Test-Setup.ps1` requires no existing registered Citrus installation. It installs silently into a unique generated directory without startup or launching a second tray instance, tests installed conversion/UI behavior with Python removed from PATH, reinstalls, and uninstalls. It preserves the existing development tray app. See the generated logs and previews under `artifacts/install-check-*`.

`Test-TrayLifecycle.ps1` launches the real executable on an isolated Windows desktop, checks that a duplicate background launch exits while the first instance survives, then verifies graceful shutdown through the installer's command path. It leaves the user's interactive tray instance alone.

Local validation passed: all 71 worker profiles using bundled engines, the app regression suite from the installed copy, install/reinstall/uninstall, and the separate real-entry-point tray lifecycle check. Those checks do not replace the clean-machine/manual gates below.

## Public-release gates

The generated installer is a local release candidate, not yet a signed public release.

- Citrus source is licensed under AGPL-3.0 (see LICENSE), as selected by the owner. Retain all third-party license files and notices included in the payload.
- Before hosting binaries, publish the exact matching Citrus source/build scripts and corresponding third-party source/build materials required by their licenses. In particular, the GPL-enabled FFmpeg build and its compiled codec dependencies, MuPDF/PyMuPDF, libheif/codec components and LibreOffice require an inventory of the actual distributed binaries. Upstream links alone are not a complete source-distribution package. This source-compliance bundle is not yet assembled.
- Sign both Citrus.exe and setup with the publisher's code-signing certificate, then timestamp and verify signatures. No certificate/private key is present. Unsigned downloads can trigger SmartScreen.
- Complete clean Windows 10/11 VM tests, including a machine without Python/LibreOffice/VC++, fresh sign-in, tray startup, upgrade while converting, uninstall while converting, blocked elevation, offline execution, Defender scanning and failed-install recovery.
- Run the roadmap's real Explorer/Desktop drag matrix, mixed DPI/multiple monitors, display edges, accessibility, RDP and nonlocal/cloud files. The current automated checks do not prove those interactions on every Windows configuration.
- Measure idle CPU/memory, startup time and installer size on declared reference machines. The current gesture observer polls every 25 ms; performance acceptance remains open.

The existing fidelity/format limits in README still apply. No universal 100% conversion guarantee is made.
