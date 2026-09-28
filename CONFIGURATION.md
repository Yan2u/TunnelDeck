# TunnelDeck — Configuration and Usage

English | [简体中文](CONFIGURATION-zh.md)

See [README](README.md) for the project overview and interface screenshots. This document retains detailed usage instructions, build and release processes, verification entry points, and historical acceptance records.

Historical validation (2026-09-23): Features and the main UI / UX passed user evaluation, and local Release x64 closeout was completed. The UI project is located at `src/TunnelDeck`; design rules are in [Unified Design](docs/ui-design-system.md), regression results are in [UI Validation](docs/ui-refresh-validation.md), and memory analysis is in [Performance Records](docs/performance.md). English / Simplified Chinese support and schema 3 were added afterward; see [Localization](docs/localization.md). The script layout below reflects the 2026-09-28 reorganization.

A concise Windows 11 / WinUI 3 SSH port forwarding assistant. Select a server, enter a port, and press Enter to start quickly; you can also save cross-server rule groups, start them all at once, and review results one by one, retrying failed items. Supports independent start/stop, incrementing when Local is occupied, Requested / Actual, 200 Recent entries, cumulative statistics, auto-save settings, resource Pin / drag-reorder, import/export, independent terminal, deletion impact confirmation, reconnection after disconnect, bounded exit, and single instance. Historical acceptance for M0–M13 is in [M13 Validation Records](docs/m13-validation.md), and runtime and data upgrade notes for the current package are in [Local Release](docs/release.md).

## Build and Run

Run in PowerShell 7:

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Run.ps1
```

You can also open `TunnelDeck.slnx` in the root directory in Visual Studio and select `Debug | x64` or `Release | x64` to build. The UI project's `Build/XamlIncrementalBuild.targets` independently records the inputs of the actual XAML compilation, invalidating old compilation caches when XAML, project paths, or build rules change, avoiding mixing design-time generated `.g.i.cs` with old `.g.cs`; incremental builds are preserved when inputs are unchanged.

The scripts use the Visual Studio Developer PowerShell specified by the project's AGENTS.md. The project remains `net10.0-windows10.0.19041.0`, x64, `WindowsPackageType=None`, and launches directly:

```text
src/TunnelDeck/bin/x64/Release/net10.0-windows10.0.19041.0/TunnelDeck.exe
```

This is a development run path that depends on an installed .NET / Windows App Runtime and has not yet been verified for release on a clean machine.

## Repository Layout and Scripts

| Directory | Purpose |
| --- | --- |
| [scripts/](scripts/README.md) | Six daily entry points: Build, Run, Test, Publish-Release, Clean-GeneratedArtifacts, and Enter-DevelopmentShell. |
| [scripts/debug/](scripts/debug/README.md) | UI regression, preview windows, performance tools, and shared helpers. |
| [scripts/fixtures/](scripts/fixtures/README.md) | Start-SshFixture / Stop-SshFixture and their Dockerfile / Python SSH test service. |
| [scripts/packaging/](scripts/packaging/README.md) | Check-Prerequisites and Smoke-Test, copied into release packages. |
| [tests/](tests) | Compiled Core, Infrastructure, and SSH test projects. |
| [test-infra/TunnelDeck.ProcessProbe/](test-infra/TunnelDeck.ProcessProbe) | A compiled helper used by infrastructure tests to inspect process arguments. |

Run the documented commands from the repository root. `Test.ps1 -Ssh` manages the fixture lifecycle automatically; direct entry points are `scripts/fixtures/Start-SshFixture.ps1` and `scripts/fixtures/Stop-SshFixture.ps1`. Test output stays in `.test-output/` and `artifacts/`.

## Release x64 Publishing

The UI project in Visual Studio uses the `ReleaseX64` publish configuration; automatic packaging uses:

```powershell
./scripts/Publish-Release.ps1 -Verify
```

By default, it generates `artifacts/release/TunnelDeck-<project version>-win-x64.zip`, a SHA-256 checksum, and a file manifest. The script locks dependencies, checks the compiled XAML resources, and runs startup, editing, old configuration upgrade, and restart checks after extracting from the ZIP. The release package depends on shared runtimes and is locally unsigned; there is no automatic upload or creation of an external Release.

The source files `scripts/packaging/Check-Prerequisites.ps1` and `scripts/packaging/Smoke-Test.ps1` are copied beside `TunnelDeck.exe` at the package root. Their packaged filenames and commands are unchanged; `docs/release.md` becomes the package README. `-Verify` runs `scripts/debug/Verify-Release.ps1`, which invokes the extracted package's smoke script.

Before validating a new schema 3 release, update the existing schema 2 metadata in `Publish-Release.ps1` and the upgrade assertions in `Smoke-Test.ps1`. The directory reorganization verified script paths, package-file copying, and prerequisite detection; it did not rerun the GUI smoke or produce a new release archive.

The historical [TunnelDeck-1.0.0-win-x64.zip](artifacts/release/TunnelDeck-1.0.0-win-x64.zip) was built on 2026-09-23 and predates localization. Its acceptance summary is in [Closeout Evidence](docs/evidence/2026-09-23-release-closeout.json); use the current source build for the latest functionality.

## Verification

See [Scripts Directory](scripts/README.md) for daily scripts and dependency notes. UI regression, evaluation samples, and performance tools are centralized in [scripts/debug](scripts/debug/README.md) and run as needed.

Some historical UI scripts still assume earlier text, layouts, or schema versions. Update those assumptions before using them against the current application; old acceptance results do not imply a current full regression pass.

```powershell
# Core and Windows credential tests
./scripts/Test.ps1

# Automatically start and clean up real OpenSSH fixtures in WSL Ubuntu / Docker
./scripts/Test.ps1 -Ssh

# Reuse the image when the fixture image has already been built
./scripts/Test.ps1 -Ssh -NoFixtureBuild

# View layout previews; switch between Sample / Long lists / Empty at the top
./scripts/Run.ps1 -Preview Sample

# Manually view window test data that is explicitly annotated
./scripts/Run.ps1 -WindowProbe
```

SSH fixtures use random temporary credentials, independent containers, and dynamic ports bound only to loopback. Containers and temporary credentials are deleted after tests end; logs are in `.test-output/`, and test reports and screenshots are in `artifacts/`. Tests do not read user SSH configuration or use actual TunnelDeck user data.

M12 / M13 baseline: 663 tests passed, and the Release x64 locked build had zero warnings and zero errors. The historical import window-shrinking anomaly was identified in M13 as resetting the list during focus switching, and after the fix the full Transfer and loop checks passed. See [M13 Records](docs/m13-validation.md) for scale, performance, native regression, and items still requiring acceptance; [M12 Original Evidence](docs/evidence/2026-09-21-m12-authentication-defaults.json) retains the original failure facts.

## Data and Recovery

Normal configuration is stored in `%LOCALAPPDATA%\TunnelDeck\state.json`, and the previous `.bak` is retained during atomic replacement. If the main file is corrupted, a valid backup can be loaded and a prompt displayed; if both are corrupted, a retryable error is shown, and unknown versions remain read-only. Normal edits save successfully before updating the interface, and exit waits for accepted changes.

The internal configuration is schema 3. When loading old configurations, missing authentication defaults and English language settings are supplemented in memory, and the new format is saved only on the first actual edit while retaining the old backup. The portable export format remains v1.

Select English / Simplified Chinese in Settings → Appearance → Language, and after saving it switches immediately without restart. The default is English and does not automatically follow the system; see [Localization](docs/localization.md) for details.

Passwords / private key passphrases are stored in Windows Credential Manager, and the configuration stores only the Guid; private keys reference only paths. Host trust is saved in a separate `host-keys.json`. Credential cleanup failures are recorded by a local queue and retried on subsequent startups. After restoring an old configuration, old credentials may need to be re-entered. See [storage](docs/storage.md) for the complete conventions.

The window probe automatically uses a temporary directory and an isolated credential namespace; when a diagnostic data directory needs to be specified, use `./scripts/Run.ps1 -DataDirectory <absolute path>`.

`-Preview Sample|Stress|Empty` also isolates data and does not establish SSH connections. Network commands are explicitly disabled; saving, Pin, menu movement, and basic settings can actually be used. See [quick-forward](docs/quick-forward.md) for real forwarding usage in normal mode, temporary credentials, and history conventions, and see [shell-and-navigation](docs/shell-and-navigation.md) for pages / navigation.

Profile editing uses an independent draft, and Apply rule does not directly write to disk; Save saves the name, rules, and order all at once. Only saved and unmodified Profiles can be started, and existing tunnels continue to use the original snapshot. Each Profile start operation counts as one use only if at least one new instance succeeds; pure duplicates, visiting pages, and Pin do not count. See [profiles](docs/profiles.md) for details.

Delete / Delete all are located under More. Server deletion first shows the impact, cancels related pending startup work and stops forwarding, then commits the configuration at once; if stopping or saving fails, the configuration is retained, stopped forwardings remain stopped, and retry is possible after rechecking. Profile deletion preserves servers, existing forwardings, and source snapshots. See [deletion](docs/deletion.md) for details.

Established forwardings automatically reconnect after transient disconnects: one recovery group per server, exponential backoff from 1 second to 30 seconds, total window of 2 minutes, preferentially restoring the last actual local port; permanent errors such as authentication / host key wait for manual Retry, and they do not revive during Stop / Delete / exit. Closing the window exits: first disable new commands, then clean up and save within a 10-second budget, and on failure provide Retry or an explicit exit without saving. Only one instance runs per data directory, and launching again restores the existing window. See [lifecycle](docs/lifecycle.md) for details.

## Import, Export, and Terminal

Import / Export are located under More on the All page. Profile export includes the required servers, and the JSON does not include secrets, trust, or runtime state; import creates new IDs, does not overwrite by name, and rejects invalid JSON as a whole batch. SSH config can be imported if the necessary connection fields are valid, while other unsupported options are ignored and prompted; only missing / invalid necessary fields need to be completed or skipped, and the local SSH config is not written back. See [Import Export](docs/transfer.md) for details.

Settings → Server authentication can set the default Password / Private key. After selecting a key, the default file is displayed, `%USERPROFILE%\.ssh\id_rsa` is automatically detected, and browsing to select or manually entering a path is supported. New Servers and SSH config entries that do not declare `IdentityFile` use these defaults; a valid explicit `IdentityFile` takes precedence, and existing Servers and explicit authentication in JSON keep their original values.

Recent preserves the source name at the time the event occurred, so it can still be recognized after deletion / renaming; total statistics are unaffected by Recent trimming, and Most used ranks only current resources. Port defaults affect only new Quick / Rule entries, authentication defaults only prefill new Server / SSH imports, and existing drafts are retained; see [Activity and Settings](docs/activity-and-settings.md) for details.

The Open terminal button in Quick Forward and More in Server details can open an independent SSH terminal. The terminal handles authentication and known_hosts itself and does not receive saved passwords; exiting the application and closing the terminal do not affect each other. See [Open Terminal](docs/terminal.md) for details.

## Cleaning Generated Files

After tests end and necessary acceptance summaries are retained, old logs, screenshots, temporary fixtures, and build directories in the workspace can be previewed and cleaned:

```powershell
./scripts/Clean-GeneratedArtifacts.ps1 -IncludeBuildOutputs -WhatIf
./scripts/Clean-GeneratedArtifacts.ps1 -IncludeBuildOutputs
```

The script retains release packages and validation summaries in `artifacts/release`, source code, `docs/evidence`, and Visual Studio settings. Before deletion, it checks absolute paths, reparse points, tracked files, and processes in use; SSH fixtures must be stopped normally first. After cleaning build outputs, rebuilding is required before running the development version; release packages can be used directly.

## Current Technical Boundaries

- SSH.NET is pinned to `2026.0.0`. Local rules can share connections; Remote rules each use an independent SSH transport to avoid upstream global reply crosstalk.
- Unknown host keys reject the handshake first, and after confirmation in the native dialog, revalidation and authentication occur; connection is rejected when a saved fingerprint changes. Test Connection uses the current draft, and the temporary connection is closed before a successful return, generating no active tunnel. See [connections](docs/connections.md) for details.
- When multiple rules first connect to the same fingerprint, they share one confirmation, while Remote physical connections remain independent; canceling one request does not cancel confirmation for other requests.
- When no password is saved, credentials for this session are entered before startup, and Quick does not cache or save secrets; existing Local transports can be reused. See [ADR 0002](docs/decisions/0002-forwarded-channel-keepalive.md) for compatibility handling of forwarded-channel liveness detection at the pinned version.
- Automatic incrementing is used only for confirmed occupation of the Local listening port. Remote occupation, prohibited forwarding, and PermitListen rejection all produce generic errors; within the scope confirmed by the user, **Remote does not automatically increment**, and after failure it stops and releases the transport, waiting for manual handling.
- Timeouts or cancellations do not reuse old transports; the application deadline for Remote Stop must be shorter than the library's internal timeout.
- Establishing a listener does not mean the target service is healthy. Request errors are aggregated separately; SSH.NET's Local target rejection notification is incomplete, so it cannot be used to guarantee that every target failure appears in the application. M6's 1 MiB payload, concurrent clients, cancellation, and listener release have real SSH verification; see [tunnels](docs/tunnels.md) for details.

See [baseline](docs/baseline.md) for complete commands and environment, [milestones](docs/milestones.md) for current progress, [domain-model](docs/domain-model.md) for the M2 contract, and [SSH Control Decisions](docs/decisions/0001-ssh-control-isolation.md) for the basis of key changes. Product specifications are kept in [.codex/docs](.codex/docs/implementation-plan.md).
