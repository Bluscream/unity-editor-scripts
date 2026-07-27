# Project Rules

- **Git Push Policy**: NEVER run `git push` automatically. Only commit changes (`git commit`). Only push when the user explicitly asks to push.
- **Size Calculation Comments**: Always add a comment denoting the human-readable size to byte size calculations (e.g. `MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB`).
- **Github Actions dont work**: they will not succeed, please do those actions manually instead.
- **Release Upload Policy**: Do NOT build, upload, or publish package releases or update gh-pages automatically after fixes. Only perform release uploads when the user explicitly asks to release/upload.