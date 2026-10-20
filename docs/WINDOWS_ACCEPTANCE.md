# Fresh Windows acceptance test

Status: **not run on a clean machine**. Existing developer-machine checks are not equivalent. No clean VM/spare PC was available on 20 October 2026, and the current session cannot access Hyper-V administration.

Run this with a source-compliant release candidate when a suitable Windows environment becomes available. Do not distribute the current engine-inclusive installer to outside testers before its outstanding source requirements are resolved.

## Environments

Use a fresh Windows 11 x64 VM or spare PC without Python, FFmpeg, LibreOffice, developer tools or a previous Citrus installation. A VM snapshot makes repeat tests easier. Record Windows edition/build, architecture, account type, screen scaling and display count. A Windows 10 run is separate evidence and remains required before claiming that platform was tested.

Create a second snapshot/account with a standard user and missing Visual C++ runtime if available. Do not remove system runtimes from the developer's everyday PC to simulate this.

## Procedure

1. Record the installer SHA-256 and release/source commit. Install using normal double-click, without manually elevating Citrus. Record any SmartScreen, antivirus, UAC or restart prompts. Do not tell testers to disable security tools.
2. Keep sign-in startup selected and finish. Confirm one Citrus tray icon, a responsive tray menu, and no required main window. Windows may put the icon inside its overflow menu.
3. Sign out and back in. Confirm Citrus starts without opening the main window. Reopen from Start; confirm it activates the existing instance rather than creating another tray process.
4. Make a test folder containing disposable JPG/PNG, TXT/DOCX, PDF, WAV/MP4, CSV/XLSX, SRT and ZIP files. For each relevant family, run a representative conversion, open the result in an independent viewer and confirm the source remains unchanged. Verify multi-page PDF exports include every page.
5. With the main window closed, drag a JPG from Explorer, tap and release Shift while holding the mouse, and drop onto PNG. Repeat from Desktop, at display edges, and at available 100/125/150/200% scaling. Test a second monitor if available. Record unsupported environments rather than marking untested cases passed.
6. Try Escape, a drop outside the wheel, a corrupt file, a duplicate output name, multiple selected files and More-page navigation. Confirm no unintended file moves/deletions and useful error messages.
7. Disconnect networking after installation. Repeat image, PDF, Office, media and archive conversion using fully local files. Cloud placeholders and network shares do not establish offline behavior.
8. Queue several conversions, cancel, and confirm no child worker remains and the originals survive. Retry a valid conversion afterward.
9. Reinstall the same release and upgrade to a newer candidate when available, both while idle and while a disposable long conversion is active. Confirm cancellation/exit, no locked-file errors, and no duplicate startup entry. Record the versions tested.
10. Uninstall while idle, and separately from a snapshot while conversion is active. Confirm app files, shortcuts and its own startup entry are removed; converted files and user settings remain. Reinstall and verify normal operation.
11. From a fresh snapshot with the prerequisite absent, test accepting and declining the Microsoft runtime prompt. Confirm declining gives a useful installer error rather than a broken success state. Record restart behavior if requested.

## Report

Copy this into a GitHub issue. Do not include private files, usernames, full personal paths or credentials.

```text
Release/source commit:
Installer SHA-256:
Windows edition/build / CPU architecture:
Fresh machine or snapshot description:
Account type / scaling / monitors:
Prerequisites initially installed:
Install / sign-in startup / duplicate launch:
Explorer/Desktop Shift-drag:
Formats tested and independent viewers used:
Offline conversion:
Cancellation / corruption / collision handling:
Reinstall / upgrade versions:
Uninstall / original preservation:
Security/UAC/restart prompts:
Failed or untested steps:
Reproduction steps and screenshots (optional):
```

Only mark individual cases passed after observing them. Preserve failures and attach the actual evidence; a filled checklist is not automatic certification.
