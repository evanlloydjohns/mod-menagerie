# The Mod Menagerie

A Windows desktop status center for Minecraft Java modpack upgrades. Track mods and datapacks from Modrinth, compare current and target Minecraft support, investigate blockers, and record scoped manual decisions. It never installs mods or touches game directories.

## AI development disclosure

The Mod Menagerie was built using AI (OpenAI Codex), which generated substantial portions of the application code, tests, and documentation under human direction. AI-generated work can contain mistakes or omissions, even when tests pass. Review the code and independently verify compatibility results before relying on them for a modpack upgrade.

## Requirements

- Windows 11 x64 (tested on build 26200). WPF/.NET 10; other Windows versions and architectures have not been validated.
- To build: .NET 10 SDK and internet access for the initial NuGet restore.
- Framework-dependent builds require the .NET 10 Desktop Runtime. The self-contained publish below includes it.
- Internet access for Modrinth search, URL resolution, reference-list updates, and refresh. Saved packs, metadata, decisions, and results remain usable offline.

## Build, run, test

Run these commands from the solution directory in PowerShell:

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet run --project src/ModMenagerie.Desktop
dotnet test -c Release
```

Deterministic tests cover compatibility, loader/distribution matching, prerelease channels, readiness, scoped decisions, SQLite transactions/migrations, cancellation, stale contexts, partial failure, rate limiting, save failure, dependencies, safe pack import, history retention, and evidenced version ranges. The live API test is skipped by default. To run it explicitly:

```powershell
$env:MENAGERIE_LIVE_TEST = '1'
dotnet test --filter FullyQualifiedName~LiveModrinth
Remove-Item Env:MENAGERIE_LIVE_TEST
```

For a repeatable 100-project desktop test with delayed requests and intentional partial failures, run `dotnet run --project tools/ModMenagerie.SmokeHarness -c Release`. This development-only host labels all data as synthetic, uses its own database beside its build output, and makes no Modrinth requests. It is not included in the application publish.

## Publish for Windows

```powershell
dotnet publish src/ModMenagerie.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Run `artifacts/publish/win-x64/ModMenagerie.Desktop.exe`. Keep the whole publish folder together: native SQLite and .NET files are required. Copying that directory provides a portable distribution; no installer or administrator access is required. The executable is unsigned. A single-file/trimming build is intentionally not used for WPF.

## Use it

1. Choose **New modpack**, enter a name, separate current and target Minecraft identifiers, and one loader. **Update version choices** fetches full Minecraft releases and supported mod loaders; these choices are cached. You can type exact Minecraft identifiers when offline.
2. Choose **Add project**. Search Modrinth or paste a main `https://modrinth.com/mod/slug` or `/datapack/slug` URL. Select and preview identity before adding. For a project distributed both ways, choose the form you intend to track. One provider project appears once per modpack.
3. Choose **Refresh modpack**. Progress reports successes, failures, and canceled/unprocessed projects. Completed per-project work is saved independently. The existing table stays available during retrieval; it updates at completion, cancellation, or navigation.
4. Read the whole-modpack summary. Stable, beta, and alpha count as compatible; unknown and negative statuses block readiness. Ignored rows are excluded. Empty/all-ignored packs have no percentage.
5. Select a row for current-versus-target evidence, latest overall release, metadata, links, and **Manual decision**. Notes and evidence URLs are optional. Select **Use automatic status** and save to reset. Overrides survive refresh and apply only to that modpack/project/target/loader/distribution.
6. Combine local search with status, type, manual, supported-loader, category (including `library`), license, lifecycle, and link-presence filters. Click column headers to sort; **Columns** shows optional metadata. Drag column boundaries to resize. Search, filters, sorting, visibility, widths, and selected modpack persist on normal close; column order is session-only. **Clear filters** resets filtering, not the collection summary.

“Latest overall” is not necessarily a release for either Minecraft version or your loader. A manual positive does not invent a release. Readiness is evidence of individual project support, not proof that the pack runs or its dependencies are satisfied.

## Phase 2 workflows

- **Import .mrpack:** choose a local exported pack and review the identified projects, duplicates and unresolved entries. Choose a new or existing collection. New collections use the imported current Minecraft version and loader; enter the target separately. Existing collections must match the imported current version and loader; their target and decisions are preserved. Check the projects to include, import, then refresh. Only metadata is read; nothing is extracted or installed.
- **Dependencies:** after refreshing, choose **Analyze dependencies**. The indented table checks the selected target releases and shows required, optional, embedded and incompatible relationships. Select an untracked required dependency and choose **Add selected missing project**, then refresh and analyze again. This separate count does not replace individual-project compatibility.
- **History:** browse dated observations, filter by current/target/loader context, and compare automatic/effective states, selected releases, manual evidence and membership changes. Up to 200 observations per collection are retained for 365 days. History begins when this version is used; earlier progress cannot be reconstructed.
- **Version-range evidence:** select a project and open this action in its details. Select the specific cached release, enter an explicitly supported numeric family or inclusive range, and record an authoritative HTTPS source and note. The source must actually state the claimed support; the app validates syntax, not the author's claim. The resulting evidence shows its provenance. Manual decisions still take precedence.

See [Phase 2 behavior and boundaries](docs/PHASE2.md). To smoke-test these workflows in an isolated database with real Modrinth metadata, run `dotnet run --project tools/ModMenagerie.SmokeHarness -c Release -- --live-phase2`. This development host uses `phase2-data` beside its build output and does not change your regular collection.

## Local data, logs, and backup

- Database: `%USERPROFILE%\.modmenagerie\menagerie.db`
- Diagnostic logs: `%USERPROFILE%\.modmenagerie\logs\yyyy-MM-dd.log`
- Timestamps are stored as UTC offsets and displayed in local time.
- User-authored records and provider caches occupy separate SQLite tables. Schema upgrades use transactions and `PRAGMA user_version`. Unknown future schemas are refused; the app never resets an existing database to recover from an error.
- Close the app before backing up or restoring. Copy the entire `.modmenagerie` data directory to a safe location. Restore while the app is closed. Do not replace a live database.
- A single app instance is allowed per data directory. For isolated developer testing, set `MOD_MENAGERIE_DATA` to a separate absolute folder before launching. This changes both database and log locations.

## Boundaries and limitations

- Exact provider identifiers only: `1.21`, `1.21.1`, and `1.21-pre1` are different. No family/range inference from titles, descriptions, changelogs, or filenames.
- Failed checks become Unknown; last successful evidence and manual decisions are retained. Cached results have dates and do not silently expire.
- Full descriptions are displayed as safe plain text, including Markdown/HTML source; no embedded scripts or remote page rendering.
- Optional icons depend on Windows image codecs/network availability and may be absent offline. Missing optional metadata does not invalidate compatibility evidence.
- Requests are serialized, with three maximum attempts for transient failures and cancelable server-directed backoff. Large packs may take time. There is no scheduled refresh.
- Dependency analysis checks declared relationships for selected releases; it does not search alternative combinations or prove runtime compatibility. Unresolved imports and unidentified override files are not tracked and therefore are not included in readiness totals.
- Other providers, accounts, sync, notifications, scheduled refresh, and game-file management remain outside scope.
- SQLite stores small structured records as JSON in relationally keyed tables; it is not intended as a public querying/export format.

See [architecture](docs/ARCHITECTURE.md) and [verification record](docs/VERIFICATION.md) for design decisions, test evidence, and validation limits.

## Appearance
Dark mode is the default. Use the Light mode / Dark mode button at the top right to switch instantly. Your choice is saved locally and restored on startup. Standard controls and dialogs use WPF Fluent theming; status colors adapt to the selected palette.


Choose Forest, Ocean, Teal, Violet, Rose, or Amber using Accent color at the bottom of the sidebar. The sidebar, refresh button and readiness bar update immediately; compatibility status colors retain their meanings. The choice persists across restarts and works with either theme.


## Start menu installation (current user)

After publishing, close any installed copy and run:

```powershell
./tools/Install-Local.ps1
```

This copies the complete publish folder to `%LOCALAPPDATA%\Programs\The Mod Menagerie` and creates `The Mod Menagerie` in your Start menu. Search for that name in Windows Search. To pin it, right-click its running taskbar icon and choose **Pin to taskbar**. The executable includes an app icon.

Rerun the same script after publishing updates. This is a local-folder installation, not a registered installer or automatic updater. Your database remains separately stored in `%USERPROFILE%\.modmenagerie`. Removing the program folder and Start menu shortcut does not remove that database.
