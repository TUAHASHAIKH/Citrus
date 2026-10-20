# Public announcement and installer status

Updated 20 October 2026.

| Item | Status |
|---|---|
| Open-source repository and AGPL license | Ready for a source-preview announcement |
| LinkedIn copy and demo outline | Prepared in LINKEDIN_POST.md; owner posts manually |
| Windows setup candidate | Built locally; not cleared for public distribution |
| Local conversion checks | Passed 71 worker profiles with representative generated fixtures |
| Local installer checks | Install, installed-app checks, reinstall and uninstall passed |
| Real tray entry point | Background launch, duplicate launch and graceful shutdown passed on an isolated desktop |
| Fresh Windows tests | Pending: no clean VM/spare PC available; Hyper-V access denied in this session |
| Third-party source requirements | 13 Python archives, 5 native archives and 14 notices collected; binary correspondence/build-material review remains open; see SOURCE_REVIEW.md |
| Code signing | Not configured; unsigned distribution is possible with accurate disclosure, subject to the other release gates |
| Public installer hosting | Not published |

The safe announcement today is an open-source development preview linking to the repository. Do not announce the installer as publicly downloadable or fully tested on clean Windows machines.

The owner has authorized publishing source commits to the existing repository. This preparation does not send a LinkedIn post or publish a binary release.
