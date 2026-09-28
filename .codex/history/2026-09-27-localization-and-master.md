# 2026-09-27 · Chinese localization and branch consolidation

## Request and scope

Add Simplified Chinese alongside English, with a persistent Settings selector that updates the existing window immediately. Preserve connection behavior and user data. After localization, consolidate the foundation branch into `master` without discarding the extensive uncommitted M10–M13, UI, release, and localization work.

## Implementation

- Added 722 matching English / Chinese resource pairs in `src/TunnelDeck.Core/Localization`, shared through an explicit-culture .NET ResourceManager. SDK satellite resources work with the existing unpackaged WinUI application.
- Added weak language notifications to presentation models and `LocalizedStrings.Values` one-way bindings throughout native XAML. No window or Frame recreation is used. User resource names, addresses, paths, fingerprints, protocol keys, and runtime snapshots retain their original values.
- Added Settings → Appearance → Language. It uses the existing settings transaction and applies only after a successful save. Language is explicit, defaults to English, and does not follow Windows settings.
- Upgraded AppState to schema 3 with strict schema 2 → 3 language migration; schema 1 chains through the authentication-default migration. Loading preserves original files; a real edit saves the new version and keeps the backup. Export, host-key, and credential-cleanup formats stay at version 1.
- Retained invariant backend diagnostics and translated application-owned messages at the display boundary, including historical activity, field validation, import notes, deletion notices, and storage errors.
- Fixed the user-reported omissions: existing navigation items, both search placeholder forms, Dashboard Usage / No usage yet, and resource-command Add / Import / Export labels. Removed the obsolete foundation `.resw` catalog.
- Replaced root-indexer change notification with a named resource-map notification. Added live selected-option templates because WinUI caches a ComboBoxItem's rendered string. Explicitly refreshed native title-bar / search accessible names and localized switch state content.
- Kept status glyph/color selection on invariant keys while translating visible state text. Usage placeholders are now derived using the requested culture, rather than cached in the initial language.
- During regression, the existing profile-shutdown test exposed a cancellation propagation race: a late credential response could register an already-cancelled tunnel while linked server-work callbacks were still dispatching. Added an immediate operation-token check before starting a tunnel; the existing cancellation assertions remain intact.

## Verification

- Final locked Release x64 solution build: 0 warnings, 0 errors.
- Core: 441 passed. Infrastructure: 154 passed. Total: 595 passed, 0 skipped.
- New checks cover catalog parity / placeholders, XAML labels and accessible text, supported-language validation, schema migration / preservation / reload, live existing-row text, invariant status keys, user names / paths, import notes, and time / usage presentation.
- Real WinUI checks used `.test-output/localization/smoke`, not the user's normal configuration. English → Chinese → English → Chinese switching was checked in one process, including existing navigation, search, Settings headings and selected options, and On/Off text.
- Both All pages' Add / Import / Export commands and the Chinese import preview were checked. Expanded notes displayed localized line labels and messages, preserving Compression / ForwardX11 and the literal endpoint; the authentication footer was Chinese. The test document was previewed only.
- Final rebuilt instance reopened in Chinese; the sidebar, search, Usage heading and both No usage yet placeholders were verified from its native accessibility tree.
- Additional narrow-window and Quick-draft roundtrip UI actions were stopped when user input interfered with window automation. These are not claimed as completed. Core tests cover snapshot preservation; the previous layout regressions remain the baseline. No real SSH or Windows language / display-setting regression was repeated.
- The September 23 release ZIP was not rebuilt or replaced. A final Release x64 instance remains open with isolated data.
- Removed 16 localization extraction scripts, intermediate catalogs, branch-check scratch files, and superseded test reports. Retained the final Core / Infrastructure TRX reports, the small SSH preview fixture, and the active isolated app data.

## Git preservation

Confirmed that `master` at `2039b14` was an ancestor of the foundation branch at `8665720da5a09bbffa99b1a1b2a11894cd287d9e`. Advanced only the master ref with an expected-old-value guard, switched to the identical commit, and deleted the merged `codex/m0-m1-foundation` branch.

SHA-256 comparison across 551 tracked / untracked file paths, plus unchanged HEAD, index diff, and working-tree status, passed both after switching and after deletion. `master` is now the only local branch; there are no configured remotes. No staging, commit, reset, clean, or stash was performed. The detached M13 baseline worktree and the user's `.codex/prompt/260919.md` are retained. Subsequent edits only finalized this record and the current handoff / validation summary.
