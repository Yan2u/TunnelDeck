# Test fixture script organization

## Purpose and assessment

The user asked whether files in `test-infra/` and `tests/` are necessary and requested centralizing scripts under `scripts/`.

`tests/` contains three compiled C# test projects and their supporting source files, with no standalone scripts outside generated output. `test-infra/TunnelDeck.ProcessProbe/` is a compiled executable used by infrastructure tests for process-argument checks. Both remain in place with their project references intact.

The two PowerShell fixture entry points and the Python control service are required by real SSH tests. Their Dockerfile belongs with the Python service as one build context.

## Changes

- Moved `Start-SshFixture.ps1`, `Stop-SshFixture.ps1`, and the complete two-file `sshd/` build context from `test-infra/` to `scripts/fixtures/`.
- Adjusted the two PowerShell scripts' repository root calculation for the additional directory level. Their parameters, output isolation, container identity checks, and credential cleanup behavior are unchanged.
- Updated `scripts/Test.ps1` and the Connections, Forwards, Profiles, Lifecycle, Deletion, and application-performance callers.
- Added `scripts/fixtures/README.md`; updated the script catalog, debug guide, project overview, baseline commands, cleanup message, implementation-plan directory map, and handoff.
- Preserved historical evidence and history paths. The six daily entry points remain at the top level of `scripts/`.

## Validation

- Compared SHA-256 hashes for all four migrated files against their original content plus the two expected root-path substitutions: all match.
- Parsed all 55 PowerShell scripts and resolved 136 static script / project references.
- Confirmed the four migrated fixture files and their README are visible to Git, and the process-probe project references still resolve.
- Entered the documented Visual Studio development shell, then ran the relocated Start script with image building enabled. Docker loaded the relocated context and reused the unchanged image layers successfully.
- The new isolated container reported healthy OpenSSH services. The relocated Stop script completed, removed its container and disposable credentials, and removed the manifest. Only sanitized diagnostic logs remain in the task-owned ignored output directory.
- Whitespace checks passed. No full GUI / SSH regression or application rebuild was needed for this directory-only change.

Application source, C# tests, user data, SSH configuration, and previous uncommitted changes were preserved. Nothing was staged or committed.
