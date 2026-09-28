# Packaging script organization

## Purpose and changes

The user requested moving the remaining packaging scripts into the centralized script directory.

- Moved `packaging/Check-Prerequisites.ps1` and `packaging/Smoke-Test.ps1` unchanged to `scripts/packaging/` and removed the empty legacy directory.
- Updated the two source paths in `scripts/Publish-Release.ps1`. The published layout is unchanged: both scripts still sit beside `TunnelDeck.exe` and the package README.
- Kept `Smoke-Test.ps1`'s package-relative install-directory default and its reference to the sibling prerequisite script unchanged. `scripts/debug/Verify-Release.ps1` still runs the script from the extracted package root.
- Added `scripts/packaging/README.md`; updated the script catalog, source-release documentation, and handoff.

## Validation

- SHA-256 hashes of both moved scripts match the original files exactly.
- All 57 PowerShell scripts parse; 136 static script / project dependencies resolve.
- Executed the three actual Copy-Item statements extracted from Publish-Release in a fresh task-owned `.test-output/packaging-layout/` directory. Both script copies match their sources and the package README matches `docs/release.md`.
- Ran the relocated Check-Prerequisites script through Windows PowerShell 5.1 with `-Json`: all required runtimes are present; it reports no installation or settings changes.
- Confirmed the new files are visible to Git and whitespace checks pass.

This change did not rebuild or overwrite a release archive and did not run GUI smoke tests. The existing smoke script still expects schema 2 after an upgrade; its historical assertion is documented and must be updated before using it to validate the current schema 3 application. No application code, user data, or existing uncommitted work was changed, staged, or committed.

## Bilingual documentation follow-up

At the user's request, synchronized the existing `README.md`, `README-zh.md`, `CONFIGURATION.md`, and `CONFIGURATION-zh.md` without replacing their previously translated content.

- Added reciprocal language links and corrected README links to the actual lowercase `.md` filenames. Chinese pages now link to the corresponding Chinese overview / configuration guide.
- Documented `scripts/debug/`, `scripts/fixtures/`, and `scripts/packaging/` consistently, including which compiled test projects remain under `tests/` and `test-infra/`.
- Explained the unchanged package-root script layout, historical ZIP status, and existing schema 2 publishing metadata / smoke assertions that must be adapted before validating a schema 3 release.
- Aligned the Chinese README's Profile terminology with the application's “配置组” label.
- Checked 104 local links, including exact filename case; both language pairs reference matching local resources, and all four PowerShell command blocks match between the configuration guides. Whitespace checks passed.
