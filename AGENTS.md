# Project workflow

- The user authorizes committing and pushing to https://github.com/TUAHASHAIKH/Citrus.git after each major completed change. Run the relevant checks, review the diff, create a descriptive commit, and push without asking again. Do not commit incomplete work merely to create a checkpoint.
- Maintain `.gitignore` as the project grows. Keep executables, build/test artifacts, user-provided images, local IDE files, secrets and signing credentials out of Git. The small `Try Citrus/Sample.jpg` fixture is intentionally public.
- Preserve the working background app and Windows sign-in startup behavior. `Try Citrus` is the user's manual-testing folder; keep developer artifacts elsewhere.
- Build and test with `tools/Test.ps1`, which uses a temporary directory. Document material limitations accurately in the roadmap and README.
- Never discard unrelated user changes or overwrite remote history. If a push is blocked, report the blocker and retain the local commit.
