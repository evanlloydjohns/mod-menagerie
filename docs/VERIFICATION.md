# Verification record

Executed on Windows 11 x64 (10.0.26200), .NET SDK 10.0.400, September 16, 2026. Tests use temporary SQLite databases and controlled provider fixtures; live checks are separate. No Minecraft game directory was used.

## Automated and live results

- Release solution build: passed, zero warnings and errors.
- Deterministic suite: 42 passed, zero failures. The opt-in live case is reported as skipped in this run.
- Explicit live Modrinth test: 1 passed, zero failures. Exercised version/loader reference lists, Sodium search and lookup, full version retrieval, and exact Fabric matching. Also resolved Terralith and verified explicit datapack release metadata independently of Fabric.
- Self-contained `win-x64` publish: succeeded. Output is `artifacts/publish/win-x64`.
- Machine-readable test records: `artifacts/test-results/deterministic.trx` and `live-modrinth.trx`.

The earlier initial package restore was blocked by sandbox network restrictions; an approved restore succeeded. Package audit identified an older transitive SQLite native package; the final dependencies use Microsoft.Data.Sqlite 10.0.12 and SQLitePCLRaw.bundle_e_sqlite3 3.0.5. No audit warnings remain in the final build.

## Hands-on Windows checks

Operated the actual WPF windows with the computer-use tool:

1. Launched the desktop app and created **Smoke Test — Fabric**, current `1.20.1`, target `1.21.1`, loader `fabric`.
2. Updated provider version choices; the live request succeeded and the fields retained their independent values.
3. Searched for Sodium. Search returned 20 of 105 results at test time; selected and resolved the project, showing team/roles and description, then added it.
4. Attempted to add the same project again; the UI reported that the duplicate was prevented.
5. Pasted `https://modrinth.com/datapack/terralith`, previewed the resolved stable identity, selected datapack distribution, and added it.
6. Before refresh, both projects were Unknown and readiness was 0/2. A live refresh reported 2 successes, 0 failures, 0 unprocessed, and stable support for both at this test target.
7. Closed and relaunched the app. The selected modpack, current/target/loader, memberships, selected releases, and dated cached results were restored without a new refresh.
8. Inspected Sodium details: current `mc1.20.1-0.5.13-fabric`, target `mc1.21.1-0.8.13-fabric`, and a different latest-overall release were displayed separately. These are observations at the test time, not hard-coded product facts.
9. Saved a manual **Not compatible** decision with a conspicuous smoke-test note. Readiness changed to 1/2 (50%) and manual decisions to 1; the automatic supporting release remained visible.
10. Changed only the target to `1.21.2`. Current stayed `1.20.1`; manual decisions dropped to 0. Candidate evidence was reevaluated from the dated cache for the new exact context.
11. Local search for Sodium reduced the table to one row while the summary remained 2/2. Combining this with Manual only yielded the correct no-matches state. Enabled the optional Type column.
12. Launched the self-contained executable from `artifacts/publish/win-x64`. It restored the saved search, Manual only filter, Type column, selected modpack, current/target settings, and dated results. This verifies the actual publish output, not only a development build.
13. Found and fixed editable version-choice prefix completion: typing `1.21.1` could select `1.21.11`. Rebuilt, reran the deterministic suite, republished, and verified exact `1.21.1` entry in the final executable. Returning to that target restored the persisted manual blocker and 1/2 readiness; current remained `1.20.1`. Cleared table filters afterward. The clearly named smoke-test modpack and its explicitly labeled test override remain available for inspection.

## 100-project responsiveness and partial failure

The development-only `tools/ModMenagerie.SmokeHarness` launches the same MainWindow and Tracker against an isolated SQLite database and an explicitly synthetic, delayed provider. It never calls Modrinth. All project names and the window title identify synthetic data.

- Full 100-project refresh completed with **90 succeeded, 10 failed, 0 unprocessed**. Readiness showed **77/100**, with **37 stable, 19 beta, 21 alpha, 13 not detected, 10 unknown**, totaling 100. Each eleventh provider request intentionally failed.
- Started a second refresh and typed `Project 05` while progress advanced from 13/100 to 28/100. The table immediately showed 10/100 rows; painting, typing, and progress remained responsive.
- Clicked Cancel while requests were in progress. The UI reported **38 succeeded, 4 failed, 58 canceled/unprocessed**, totaling 100. The completed results and prior dated rows remained available.

To reproduce:

```powershell
dotnet run --project tools/ModMenagerie.SmokeHarness -c Release
```

Its database is inside the harness output's `smoke-data` directory, not the user's app-data folder. This harness is excluded from the desktop publish.

## Acceptance coverage

| Criteria | Evidence |
|---|---|
| A01–A02 | SQLite reopen/rename/two-pack shared-ID/removal/deletion isolation tests; desktop create and restart |
| A03 | Desktop search, URL preview, duplicate prevention; unsafe/unsupported URL tests; controlled provider failures |
| A04–A05 | Context-change-during-refresh test; desktop target-only change; current value unchanged by refresh |
| A06 | SQLite reopen with cached metadata/decisions; app restart uses no provider refresh; local editing paths independent of network |
| A07 | Desktop search/combined filtering/column toggle; sort issue found and fixed; preferences stored in SQLite |
| A08–A09 | Real Sodium metadata and current/target/latest-overall distinction inspected in details |
| C01–C10 | Deterministic channel, exact-loader/game, stable preference, date/ID tie-break, no union/prefix/text inference and empty-result fixtures |
| C11–C13 | Malformed version fields, incomplete cache and failure tests; datapack fixture + live API; archived fixture |
| C14–C19 | All override choices, note/reference persistence, reset, collection/target/loader/form scope, no invented release, ignored readiness tests; desktop manual blocker |
| R01–R02 | Exact ten-project arithmetic and zero-included fixtures |
| R03–R04 | 100-project automated persistence/progress case; hands-on delayed 100-project UI host with ten failures |
| R05 | Controlled 429/503 retries, permanent-error no-retry and cancellation during rate-limit wait |
| R06–R09 | Renamed slug stable-ID fixture, 404 no-retry, stale-context and cancellation tests, absent optional-field fixtures/live metadata |
| R10 | SQLite abort trigger proves per-project transaction rollback, no success progress, and preservation of prior data |

## Validation limits

- Windows 11 x64 was the tested platform; other Windows versions, ARM64, screen readers, and a broad DPI matrix were not tested.
- No system network settings were changed to force an outage. Offline paths were checked through cached restart and deterministic failing providers; the harness has no network dependency.
- The 100-project workload used controlled synthetic metadata, not a 100-project live Modrinth request batch. Two real projects were refreshed through the desktop and the live integration test exercised the production endpoints.
- Installer signing, dependencies/runtime conflicts, actual Minecraft execution, and later-phase requirements are outside this MVP.

## Dark theme follow-up
Built and published the dark-theme update successfully. Manually inspected the dashboard and edit-modpack dialog, switched to light and back to dark, and restarted the published executable to verify the saved preference. Explicit table row backgrounds prevent white rows under the Fluent dark theme. No business logic changed.


## Accent selector follow-up
Release solution build passed with zero warnings/errors; Windows publish succeeded. Manually selected Violet from the six-preset selector and verified immediate sidebar, refresh-button and readiness-bar updates. Restarted the published app and verified Violet was restored. Compatibility colors remained unchanged.


## Local installation follow-up
Published successfully with an embedded multi-resolution application icon. Installed the complete output into the current user's local Programs folder and created a Start menu shortcut. Launched that shortcut and observed the process running from the installed path with the existing two-project pack, cached results, dark mode and current Forest accent restored. Windows Search indexing and taskbar pinning were not automated; the Start menu shortcut is in the standard per-user Programs location.
