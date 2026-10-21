# Citrus roadmap

## Implemented

- Cursor-relative Shift-drag conversion wheel, file-picker UI, tray lifetime and sign-in startup.
- Glass-style appearance with Windows composition fallbacks, theme and reduced-animation settings.
- Image, media, PDF, Office, subtitle and archive routes listed in the README.
- Isolated conversion workers, bounded queue, cancellation, source preservation and collision-safe output naming.
- Editable PDF-to-Word layout reconstruction with fonts, tables, images and page sections; exact fidelity is not guaranteed.
- Offline x64 installer bundling the conversion engines and Microsoft runtime prerequisite.
- Full upstream notices, pinned native source/build materials and published 0.3.2 corresponding sources.

## Release validation

The 0.3.2 installer passed all 71 advertised worker profiles, shell/tray checks and local install/regression/reinstall/uninstall checks. The maintainer reports successful installation and use on a friend's Windows laptop. Its Windows version and individual acceptance cases were not recorded; this is not a complete compatibility certification.

The installer is unsigned. Its setup file is published as a GitHub Release asset. The existing source-only release must remain available alongside it.

## Installer compression validation

The published compression-only update reduced the existing 0.3.2 setup from 405.5 MiB to 320.3 MiB (21.0%) with the exact same payload. Shell checks and install, installed-app regression, reinstall and uninstall checks passed. Installed size is unchanged. The compressed installer and its matching checksum replaced the assets in the existing v0.3.2 public beta release; the application version and payload remain unchanged. The original installer remains locally under artifacts/setup, and the compressed copy is under artifacts/compression-check. Additional-PC testing of this repackaged installer is still pending.

## Next priorities

- Broader Explorer/Desktop drag testing across mixed-DPI monitors, accessibility settings and privilege boundaries.
- Code signing and clearer release/upgrade verification.
- Persistent job history, crash recovery and retry.
- More detailed progress for long media conversions.
- Additional real-world PDF/Office layout fixtures and clearer font/OCR feedback.

## Future work

- OCR for scanned PDFs.
- Interactive crop, annotation, redaction and trim tools.
- Additional archive formats, including RAR where licensing and implementation allow.
- A tested update mechanism.

Citrus is an independently developed Windows application. Full Tangerine feature parity, arbitrary PDF layout fidelity and every possible file-to-file conversion are not promised. Existing limitations and supported operations are documented in README.md.
