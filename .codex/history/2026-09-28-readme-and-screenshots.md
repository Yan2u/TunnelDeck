# Project README and UI screenshots

## Purpose

Replace the development-oriented landing README with a concise English project overview following the user's WinSystemCtl example. Preserve the detailed build, usage, release, and validation material separately.

## Changes

- Moved the previous README to `CONFIGURATION.MD`, verified its contents survived the move, then added a descriptive title and a link back to the project overview.
- Wrote a new `README.md` covering core concepts, features, the three application projects, Build and Run, screenshots, and the actual direct dependencies: Windows App SDK, DevWinUI, and SSH.NET.
- Added `assets/screenshots/dashboard.jpg` and `profile.jpg`. Both are 1072 × 740 pixels, totaling 138,755 bytes, and are referenced from the README.
- Corrected `.gitignore`: the broad `[Dd]ebug/` rule also matched the previous turn's `scripts/debug/` directory. Explicit exceptions now make all 47 migrated scripts and their README visible to Git without staging them.

## Screenshot procedure

Started the existing Release build using `scripts/Run.ps1 -Preview Sample -DataDirectory <fresh isolated path>`. The task-owned data directory is `.test-output/readme-screenshots/20f615b693144300b067f6cfd44c7fdf`.

Used the Windows computer-use skill with `node_repl` and `@oai/sky` to select the returned preview window, shrink it, navigate to the dashboard and profile editor, and capture the visible application. Exported the native JPEG screenshot data directly to the two final files. No image content was fabricated or modified. Captures showing another foreground application or a navigation transition were discarded.

The screenshots use the application's built-in synthetic resources, reserved example hosts, and demonstration tunnel states. The visible design-preview notice explains that no SSH connections are opened. No user configuration, credentials, keys, or SSH config files were loaded for the screenshots. Closed the task-owned preview window after capture.

## Validation

- Inspected both final source captures for the intended page, legibility, and sensitive content.
- Confirmed both saved JPEG dimensions and file sizes.
- Checked all 39 local links in README and CONFIGURATION, including both screenshot paths.
- Confirmed the screenshot files, CONFIGURATION, and all migrated debug files are visible as untracked files to Git.
- Whitespace checks passed. No application code changed; no build or full regression suite was needed.

Existing uncommitted application work and historical evidence remain intact. No staging, commit, or publication was performed.
