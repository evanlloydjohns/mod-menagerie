# Minecraft Modpack Upgrade-Readiness Tracker

**Document:** PROJECT_SPEC.md  
**Status:** Complete specification draft for product review; not yet approved for implementation  
**Revision:** 1.1 — September 16, 2026  
**Initial platform:** Windows desktop  
**Required technology:** C# and .NET  
**Primary data provider:** Modrinth

## 1. Purpose and governing principles

Build a Windows application that tracks collections of Minecraft Java Edition mods and datapacks and answers:

> Can I upgrade this modpack from its current Minecraft version to my target version? If not, what is holding it back?

This is a **modpack upgrade-readiness tracker and status center**. It tracks metadata and compatibility; it does not manage installed game files. A typical user maintains multiple collections containing approximately 50–100 projects each, checks progress over several days, investigates blockers, and follows links to the underlying Modrinth information.

The following principles govern implementation:

1. **Make upgrade readiness the primary workflow.** Keep current version, target version, loader, counts, and blockers visible.
2. **Prefer false negatives over false positives.** Claim compatibility only from explicit structured metadata, a deliberately implemented deterministic rule, or an explicit user override. The MVP uses structured metadata and overrides.
3. **Distinguish evidence from judgment.** Preserve the automatic result when a user overrides it; visibly identify manual decisions.
4. **Count alpha and beta support, but identify it.** Compatible does not necessarily mean stable.
5. **Persist locally and work from cached information when offline.** Display freshness and failures honestly.
6. **Use clear, conventional architecture.** The code should be understandable to a user familiar with C#/.NET who wants to learn from it.
7. **Allow reasonable future web reuse without building a web system now.** Separate application logic from desktop UI; avoid speculative infrastructure.

Readiness means that tracked projects have evidence of support. It is not a guarantee that the complete pack runs without conflicts. Dependency analysis is Phase 2; runtime verification is outside scope.

## 2. Authority, interpretation, and scope boundaries

This document consolidates the decisions from the planning conversation titled **Prompt Planning Input**. It is intended to become the authoritative product specification after review. The separate implementation launch prompt will be written after that review.

**Agreed requirements** include the product purpose, Windows/C#/.NET, collections, current and target versions, loader-aware checks, release-channel distinctions, manual overrides, Modrinth search and URL entry, rich metadata, strong table controls, local persistence, manual refresh, and the priorities below.

**Proposed defaults** resolve details the conversation did not specify: candidate selection order, readiness arithmetic, datapack handling, freshness behavior, SQLite as the concrete storage choice, and some table-control priorities. These are explicitly documented here for review, not represented as verbatim prior decisions. Once accepted, they are the intended behavior.

The retrieved conversation includes the text layout and the user's approval of the status-center direction, but not the original rendered image or the entire response associated with it. The wireframe below preserves the available direction and does not claim pixel fidelity to that image.

Minecraft version numbers and project names used in examples are illustrative, not assertions about actual available releases or their compatibility.

## 3. Priority matrix

| Capability | Priority | Boundary |
|---|---|---|
| Create, rename, edit, delete collections | MVP — Required | Tracking collections only |
| Independent current and target Minecraft versions; one loader per collection | MVP — Required | Fabric is the primary use case; support other exposed mod loaders |
| Mods and datapacks; one project in multiple collections | MVP — Required | Preserve project identity independently of membership |
| In-app Modrinth search and paste-URL add flow | MVP — Required | No account required |
| Remove projects from a collection | MVP — Required | Does not remove other memberships |
| Manual collection refresh with progress and partial-failure handling | MVP — Required | No scheduled/background refresh requirement |
| Stable, beta, alpha, not detected compatible, unknown | MVP — Required | Loader and exact target version evaluated together |
| Scoped manual compatibility overrides and optional notes | MVP — Required | Includes reset-to-automatic and ignore-for-upgrade |
| Dashboard totals, readiness percentage, blockers | MVP — Required | See Section 7 |
| Project detail view, metadata, external links | MVP — Required | Missing provider fields handled gracefully |
| Local search, filters, sorting, configurable visible columns | MVP — Required | Rich metadata remains accessible without an overloaded default grid |
| Local persistence and cached data | MVP — Required | SQLite proposed |
| Build/run documentation, important tests, Windows publish output | MVP — Required | Verify the application runs |
| Group by status/type/loader, reorder columns, refresh one project | MVP — Nice to have | Do not delay required behavior |
| Direct project-ID entry and bulk URL-list entry | MVP — Nice to have | URL and search remain primary |
| Detailed current-release versus target-release comparison | MVP — Required | Provide a modest side-by-side current-versus-target view that reinforces upgrade tracking; no complex version browser required |
| Dependency analysis and dependency blockers | Phase 2 — High priority | Explicitly important; must not be forgotten |
| Import a local `.mrpack` | Phase 2 — High priority | Preferred later import flow; metadata tracking only |
| Compatibility/readiness history | Phase 2 | Include target and loader context |
| Deterministic version-range/family support | Phase 2 — Medium/High priority | Support patterns such as `26.x`, `26.3.x`, and explicit ranges only from reliable evidence and separately defined/tested rules |
| JAR-directory identification/import | Future consideration | Do not assume filenames identify projects reliably |
| Export/import tracker collections | Future consideration | Distinct from importing `.mrpack` |
| Notifications, Windows notifications, automatic background refresh, tray mode | Future consideration | No always-running process in MVP |
| Multiple loaders per collection; loader migration comparison | Future consideration | MVP has one collection loader |
| CurseForge integration | Future consideration | No multi-provider framework required now |
| Hosted web application, accounts, cloud synchronization | Future consideration | Separate product work |

Required MVP work takes precedence over optional polish. Phase 2 and future items are recorded commitments to consider later, not authorization to implement them during the MVP.

## 4. Terminology and conceptual data model

| Term | Meaning |
|---|---|
| Collection / modpack | A named local tracking list. These terms are equivalent in this application; choose one consistent UI label, preferably “Modpacks.” |
| Current Minecraft version | The version the user says the real pack currently uses. It is not discovered from disk. |
| Target Minecraft version | The full Minecraft release the user is evaluating for an upgrade. |
| Loader | The mod platform selected for the collection, such as Fabric, Forge, NeoForge, or Quilt. |
| Project | A Modrinth project identified by its stable provider ID. |
| Membership | A project's inclusion in a particular collection. The relationship is many-to-many. |
| Project version / release | A published version of a project, distinct from a Minecraft version. |
| Automatic result | Compatibility derived from provider metadata for a specific target and loader. |
| Effective result | The active manual override, if any; otherwise the automatic result. |
| Refresh outcome | Whether retrieval succeeded, failed, or remains incomplete; independent of compatibility. |

Model at least these concepts, without requiring an elaborate framework:

- **Collection:** local ID, name, current Minecraft version, target Minecraft version, loader, creation/update timestamps.
- **Project:** Modrinth ID, current slug/URL, cached descriptive metadata, lifecycle status, metadata retrieval timestamp.
- **Collection membership:** collection ID and project ID; unique within a collection. For projects with multiple distribution forms, retain the selected mod/datapack form if necessary to evaluate correctly.
- **Project version metadata:** provider version ID, project ID, displayed version number/name, publication time, channel, supported Minecraft versions and loaders/distribution form.
- **Compatibility evaluation:** collection/project/target/loader context, automatic status, reason, selected supporting version ID if any, evaluation time, data freshness and retrieval outcome.
- **Compatibility override:** collection/project/target/loader key, selected override, optional note, optional evidence/reference URL, created/modified time, local-user provenance. No account system is implied.
- **Preferences:** selected collection and practical table/display settings.

Do not conflate a project's current slug with its identity. Renames must not create duplicate projects. Shared metadata can be reused across collections, while compatibility decisions and overrides remain scoped.

## 5. Core user workflow

1. Launch the app. Restore saved collections and cached results immediately, with their timestamps.
2. Create or select a collection such as “The Wild Menagerie.”
3. Enter its current Minecraft version, target Minecraft version, and loader. Example: current `26.1.2`, target `26.3.1`, Fabric.
4. Add mods or datapacks through in-app Modrinth search or a pasted project URL.
5. Select **Refresh**. See progress, successful results, and individual errors without losing table access.
6. Read the readiness summary and filter to projects without detected support, unknown results, or prerelease-only support.
7. Select a project to examine compatibility evidence, metadata, candidate release, and Modrinth links.
8. If external evidence warrants it, apply a scoped manual override and optionally record both a note and an evidence/reference URL explaining why.
9. Return later and refresh again to see how many blockers remain.
10. Change the target for the next upgrade when desired. Reevaluate for the new context; never carry old-target overrides into it.

### Collection behavior

- Create, rename, edit, and delete collections; validate required fields and nonblank names.
- Use a Minecraft-version selector backed by provider version metadata where practical, with cached choices available offline. Default to full releases, not snapshots, pre-releases, or release candidates.
- Current and target are separate fields. Changing one does not silently change the other. Identical values are allowed and clearly shown.
- Refresh never advances the current version. The user edits it after upgrading the real pack outside this app.
- One loader applies to mods in the collection. Do not assume Fabric and Quilt, or Forge and NeoForge, are interchangeable.
- Deleting a collection removes its local memberships and overrides, not other collections or remote projects. Provide a clear destructive-action confirmation or undo.
- Adding the same provider project twice to one collection does not create duplicate rows. Adding it to another collection is allowed.

### Adding and removing projects

- Search Modrinth within an add-project view. Show enough identity information to distinguish similarly named results: name, icon, author/team where available, description, and type.
- Paginate/load more results and handle empty searches, delays, and API failures.
- Do not automatically restrict search to target-compatible projects: adding a project that is not yet ready is essential to the workflow.
- Parse supported Modrinth project URLs, resolve the project, and preview its identity before adding. Reject malformed or unsupported URLs with a useful message.
- Resolve URLs to stable IDs. Do not fetch arbitrary user-supplied hosts as though they were Modrinth API endpoints.
- A newly added project may remain “Unknown — not checked” until evaluated. Adding must not imply compatibility.
- Removing a project affects only the selected collection and its associated scoped decisions.

## 6. Compatibility engine

### 6.1 Automatic states and visual meaning

| Automatic status | Meaning | Visual cue |
|---|---|---|
| Stable compatible | At least one qualifying stable project release supports the target | Green plus text/icon |
| Beta compatible | No qualifying stable release; at least one qualifying beta | Yellow plus text/icon |
| Alpha compatible | No qualifying stable or beta; at least one qualifying alpha | Orange plus text/icon |
| Not detected as compatible | A successful, sufficiently complete check found no qualifying version | Red plus text/icon |
| Unknown | Not checked, incomplete evidence, unavailable project, failed request, or unrecognized required metadata | Neutral gray plus text/icon |

Use orange for alpha so red remains the absence-of-detected-support signal. Never communicate status by color alone. “Not detected as compatible” is not a claim that the mod cannot work.

Archive/discontinued information is a separate lifecycle badge. An archived project can retain a compatible release; inactivity alone does not establish discontinuation. Show only lifecycle facts the provider reports, and retain unavailable projects for user review rather than deleting them.

### 6.2 Matching algorithm — MVP

Evaluate each project against the selected collection context:

1. Retrieve relevant published version metadata, or use a clearly dated, sufficiently complete cache.
2. Require the **same candidate version** to support the exact target Minecraft identifier and the applicable loader/distribution form.
3. For mods, require the collection loader explicitly. Project-wide lists of supported game versions and loaders are insufficient: the two may belong to different releases.
4. For datapacks, use explicit datapack distribution support and the target Minecraft version. A normal datapack does not need a Fabric-tagged artifact merely because the collection also contains Fabric mods. Validate this mapping against the selected API schema; never infer datapack status from a name or description. This is a proposed product default.
5. Classify qualifying candidates by stable, beta, or alpha. Prefer stable, then beta, then alpha for the headline result.
6. Within the preferred channel, select the most recently published qualifying candidate; use a deterministic ID tie-break if necessary. This selection policy is a proposed default.
7. If the complete check finds no candidate, return “Not detected as compatible.” If evidence cannot support a trustworthy conclusion, return “Unknown” with a reason.

The provider's project-version schema supplies game-version lists, loaders, release channels, publication timestamps, and version IDs. Integration must validate these fields rather than guessing from display strings. See [Modrinth: List project's versions](https://docs.modrinth.com/api/operations/getprojectversions/).

“Latest project version” and “Selected compatible version” are different fields. A newer incompatible release does not erase an older compatible stable release. Do not sort arbitrary project version strings lexicographically to find the newest release.

### 6.3 Minecraft prereleases versus project prereleases

These are separate axes:

- A **beta mod release** explicitly supporting the full Minecraft target counts as beta compatible.
- A **stable mod release** supporting only Minecraft `26.3-pre1` does not count for Minecraft `26.3`.
- Support for `26.3` alone does not automatically imply support for `26.3.1` or vice versa.
- Do not round versions, compare prefixes, or assume every Minecraft identifier follows one semantic-version scheme.

### 6.4 Ranges and version families

Authors may state support for `26.x`, `26.3.x`, or `26.3.1–26.3.5`. Preserve a way for the user to act on that evidence without requiring unsafe automatic interpretation.

**MVP:** explicit structured target-version support or a manual override. Do not parse arbitrary titles, descriptions, changelogs, or filenames into a positive result.

**Phase 2:** deterministic range/family rules should be added only with clearly identified evidence sources, explicit boundaries, loader applicability, and tests for Minecraft version formats. This work is medium/high priority because authors commonly state support as `26.x`, `26.3.x`, or explicit ranges such as `26.3.1–26.3.5`. Display the rule and provenance behind a result. Do not silently enable such inference during MVP implementation.

### 6.5 Manual overrides — required in MVP

Offer:

- Use automatic status.
- Stable compatible.
- Beta compatible.
- Alpha compatible.
- Not compatible.
- Ignore project for this upgrade.

An override is keyed to **project + collection + target Minecraft version + loader**. If distribution form is explicitly selected, it must also be respected by the scope. Provide an optional note, an optional evidence/reference URL, and a visible modification time. Example note: “Author states 26.3.x compatibility on Modrinth.” The evidence/reference URL may point to the relevant Modrinth version, changelog, project page, issue, or other user-reviewed source that justified the decision.

Show the effective status with a clear manual badge. In details, show automatic status, its evidence/time, manual choice, note, evidence/reference URL when present, and scope side by side. A manual positive without a detected release must not fabricate a compatible version or download link.

Refresh updates the automatic result but does not overwrite the override. Reset-to-automatic removes the active override. A changed target or loader immediately stops the previous override from applying. Proposed default: retain prior-context overrides locally, allowing them to apply again only when returning to that exact context, with their original date visible.

Ignore is an explicit user decision, not compatibility. It excludes the project from the readiness denominator while keeping it visible and counted as ignored. A manual “Not compatible” remains a blocker even when automatic metadata indicates support.

## 7. Readiness calculation and freshness

The following arithmetic is a proposed default to make the agreed status-center behavior unambiguous:

**MVP blocker definition:** a blocker is a tracked, non-ignored project whose **effective compatibility status is not positive** for the active target/loader context. In MVP, this means `Not detected as compatible`, manual `Not compatible`, or `Unknown`. Beta and alpha are positive compatibility states and therefore are not blockers, although they remain visibly distinguished from stable support. Dependency-aware blockers are a separate Phase 2 concept and must not be implied by the MVP summary.

```text
Tracked = all project memberships in the selected collection
Ignored = memberships explicitly ignored for this target/loader
Included = Tracked - Ignored
Compatible = Included memberships whose effective status is Stable, Beta, or Alpha
Readiness = Compatible / Included × 100
```

Show compatible count and denominator alongside the percentage. Display stable, beta, alpha, not-detected/manual-incompatible, unknown, ignored, and manual-decision counts. Status buckets are mutually exclusive; manual, archive, and freshness badges are supplementary dimensions.

- Alpha and beta contribute to the compatible total. State their presence near the summary so “100%” is not mistaken for “all stable.”
- Unknown remains in the denominator and never counts as compatible.
- If `Included = 0`, show “No included projects” and no percentage, not 100% ready.
- Summary totals describe the whole collection even when table filters are active; show a separate visible-row count.
- A 100% result means all included projects have effective positive evidence. Indicate overrides, exclusions, and the MVP's lack of dependency checking.

### Freshness and failures

Store last attempted refresh and last successful evaluation separately, per project/context. Never label a partially failed refresh as a fully successful fresh snapshot.

Proposed default: a failed compatibility refresh makes the latest automatic result Unknown, while preserving the prior successful result as “Last known: …” with its date. A scoped manual override still determines effective status and retains its manual badge. An optional metadata-field failure, such as a missing icon, does not invalidate a successful compatibility check.

On offline startup, display saved results as cached, with their successful-check time; do not imply a fresh check occurred. After a failed refresh, apply the failure behavior above. Avoid an arbitrary silent expiry policy in the MVP.

When a target changes, never briefly display old-target results as new-target results. Reevaluate from adequate cached metadata or show Unknown pending refresh. Results from an in-flight refresh for an old context must not overwrite the current context.

## 8. UX and information architecture

### 8.1 Main view: a status center

Use a collection sidebar, a compact upgrade header, summary counts/progress, a project table, and an adjacent detail pane or equivalent project-details view. Keep the interface readable at ordinary Windows sizes and display scaling. Pixel-perfect reproduction is not required.

```text
+--------------------+-------------------------------------------------------------+
| MODPACKS           | The Wild Menagerie                          [Edit collection] |
|                    | Current: 26.1.2  ->  Target: 26.3.1     Loader: Fabric         |
| The Wild Menagerie |                                                             |
| Vanilla+           | 51 / 63 compatible — 81%                                   |
| Performance Tests  | Stable 47 | Beta 3 | Alpha 1 | Not detected 10 | Unknown 2  |
|                    | Ignored 0 | Manual decisions 2                              |
| [+ New modpack]    | [Refresh] [Add project]    Last successful check: ...       |
|                    |                                                             |
|                    | Search...   Status...   Type...   More filters   Columns    |
|                    | Name       Status       Compatible version    Updated       |
|                    | Sodium     Stable       ...                   ...           |
|                    | Iris       Beta         ...                   ...           |
|                    | Create     Not detected —                     ...           |
|                    |                                                             |
|                    | Details: metadata, automatic evidence, override, links     |
+--------------------+-------------------------------------------------------------+
```

The counts above are illustrative and reconcile to 63 included projects. The layout is guidance, not a mandated widget library or visual theme.

Provide useful empty states for no collections, no tracked projects, no search results, and no rows matching filters. Refresh progress should include completed/total counts and a concise failure summary. Long operations must not freeze navigation or painting.

### 8.2 Metadata and details

Expose the following where available, using a concise default grid and fuller details:

| Information | Intended presentation |
|---|---|
| Name, icon, short description, project type | Table identity and details |
| Author/team/contributors as available | Optional column and details; do not invent a single author |
| Modrinth URL, stable project ID, slug | Details with open/copy actions |
| Effective compatibility and manual indicator | Default table column |
| Automatic compatibility, reason, target/loader, evidence timestamp | Details |
| Manual override, note, evidence/reference URL, scope, modification time | Details when an override exists |
| Supported loaders and Minecraft versions | Details and optional columns |
| Latest overall project version and publication date | Optional column/details, clearly labeled |
| Selected target-compatible version, channel, publication date | Table/details |
| Current-versus-target comparison | MVP-required modest comparison showing the project’s evidence for the collection’s current Minecraft version versus the target version; no complex historical/version-browser UI required |
| Project last-updated time and downloads | Optional columns/details |
| License, categories, lifecycle status | Details and filters |
| Source repository, issues, wiki, Discord/community link | Clickable details when supplied |
| Full project description | Detail view with readable, safe rendering |
| Dependency information | Phase 2; optional raw display must not imply analysis |
| Last refresh attempt, last success, cached/error state | Table badge/details |

Treat missing values as unavailable rather than empty facts. Fetch additional author/team details only as needed. Project metadata and external-link availability depend on provider fields; use the [official project endpoint](https://docs.modrinth.com/api/operations/getproject/) as the integration reference.

### 8.3 Table controls

Required MVP controls:

- Instant local text search over cached name, description, author/team, project ID/slug, categories, license, loaders, and available link text/URLs. Search does not initiate a provider request on every keystroke.
- Combine filters for compatible/not detected/unknown, stable/beta/alpha, manual overrides, ignored, type, supported loader, library category, lifecycle/archive status, categories, and license. Provide link-presence filters for source/issues/wiki/community metadata.
- Sort by name, compatibility, project update date, downloads, and selected compatible-release date. Handle missing values predictably and keep ordering stable.
- Show/hide optional columns, resize practical table columns, and preserve preferences locally. Clear filters in one action.
- Selecting a row opens details without discarding table context.

Group-by status/type/loader and column reordering are nice-to-have MVP polish. Age since target Minecraft release may be shown later if a reliable release date is available; within one collection it is shared by every row and is not a useful project ordering by itself.

Use keyboard-accessible controls, visible focus, readable contrast, and text labels/tooltips in addition to color.

## 9. Modrinth integration and refresh

### 9.1 Integration contract

Use the official public Modrinth API, not page scraping. Select and document a supported API version/base URL when implementation starts; keep provider-specific response mapping in infrastructure.

Required operations are project search, project lookup by ID/slug, project-version retrieval, and reference metadata for game versions/loaders/categories as needed. Search has its own result/pagination semantics; do not treat a search hit as sufficient compatibility evidence. See [Modrinth: Search projects](https://docs.modrinth.com/api/operations/searchprojects/).

Use a uniquely identifying application `User-Agent`. Respect rate-limit response headers, bound concurrency, and back off on throttling. Do not hard-code assumptions that the service's quota never changes. Public tracking should not require a Modrinth login or secret. These integration constraints follow the [official API overview](https://docs.modrinth.com/api/).

Resolve and cache stable IDs; accommodate changed slugs, absent optional fields, unrecognized enum values, and unavailable projects. Retrieve sufficient version data to support the claimed result; an incomplete fetch must not become a false “no compatible version” conclusion.

### 9.2 Refresh behavior

- A prominent manual Refresh checks every tracked project in the selected collection, including ignored projects so their automatic evidence can remain visible.
- Display progress and prevent overlapping duplicate refreshes for the same context. Cancellation should stop outstanding work cleanly and retain completed results.
- Use asynchronous I/O, bounded requests, reasonable timeouts, and bounded retries for transient failures. Avoid retry loops on permanent errors.
- Deduplicate reusable provider fetches where straightforward, especially for projects shared across collections.
- Save successful per-project work even if another project fails. Preserve local collections and overrides throughout.
- Summarize successes, failures, and canceled/unprocessed rows. Provide a retry path through Refresh; targeted retry is optional polish.
- Internet outages, service errors, throttling, deleted/unavailable projects, malformed responses, and renamed projects must yield useful explanations.
- A 404 means unavailable to the app, not proof of incompatibility or permission to delete the local record.
- Errors should offer actionable text; technical diagnostics belong in a local log rather than raw exceptions in the main UI.

Do not add automatic refresh, notifications, or a system-tray scheduler as a condition of MVP completion.

## 10. Local persistence

Local persistence is required. **SQLite is the proposed default**, consistent with the discussion; another choice requires a documented reason and equivalent behavior.

- Save collections, memberships, current/target settings, scoped overrides, notes, evidence/reference URLs, cached project/version metadata, latest evaluation outcomes, timestamps, and table preferences.
- Store data in an appropriate per-user Windows application-data directory, not beside a potentially read-only installed executable.
- Use transactional writes and database schema versioning/migrations. Preserve existing user data during upgrades; never silently reset a database after a migration error.
- Enforce membership and override uniqueness. Deleting one collection must not destroy data still referenced by another.
- Keep cached provider data distinct from user-authored records so cache maintenance cannot remove overrides or collections.
- Store timestamps consistently and display them in the user's local time.
- Handle missing/unwritable storage and database errors without falsely reporting successful saves. Document where data and logs live and how to back up the local database safely.
- MVP needs latest results, not a full historical event store. Compatibility-history retention is Phase 2.

Offline use supports browsing saved collections, reviewing cached details, and local edits/overrides. Network-dependent search/add/refresh operations should explain why they cannot complete.

## 11. C#/.NET architecture

Use a supported .NET release selected at implementation time. Windows desktop is the delivery target. The UI framework was intentionally left open: WPF, WinUI 3, or Avalonia are acceptable if the choice is justified by maintainability, packaging, and the required Windows UX. Do not require cross-platform delivery.

A small conventional solution can use:

```text
src/
  ModTracker.Domain/          Models, compatibility rules, readiness calculation
  ModTracker.Application/     Collection workflows, refresh orchestration, overrides
  ModTracker.Infrastructure/  Modrinth client, SQLite persistence, cache
  ModTracker.Desktop/         Windows views, view models, composition root
tests/
  ...                        Focused domain, application, persistence tests
```

These are suggested names and boundaries, not a mandate to create a class for every noun. Domain has no UI/network/database dependencies. Application depends on domain and small service contracts. Infrastructure implements those contracts. Desktop composes them and handles presentation. MVVM is appropriate if supported by the selected framework.

Keep compatibility, overrides, counts, and refresh orchestration testable without opening a window. Keep dialogs, dispatcher calls, desktop navigation, and Windows-specific storage-location discovery out of core domain logic. Provider DTOs and ORM entities should not become the public UI contract by accident.

Small interfaces for the provider client, persistence, and clock where needed are sufficient. Avoid generic repository factories, message buses, microservices, elaborate CQRS, speculative plugin systems, and many layers of indirection. Dependency injection should simplify composition rather than dominate the codebase.

### Future web reuse

A later web application may reuse domain rules, application services, and much of the Modrinth integration. A web UI and hosted persistence may differ; reuse is a reasonable design objective, not a promise that the desktop UI or database deployment transfers unchanged.

Do not implement REST APIs, hosted services, authentication, cloud databases, multi-tenancy, or synchronization now. Do not add empty web projects. Document the existing seams and likely future changes in a short architecture note.

## 12. Phase 2 and future requirements

### 12.1 Dependency analysis — high priority

Analyze dependencies of the selected target-compatible candidate, not a union of every dependency ever declared by the project. Distinguish required, optional, incompatible, and embedded relationships, and version-specific references. The provider exposes dependency relationship data through its [dependency endpoint](https://docs.modrinth.com/api/operations/getdependencies/); validate version-specific details when implementing this phase.

Show cases such as: “Create Fly has target support, but required dependency Create is not ready.” Preserve the project's own compatibility result while adding a distinct dependency-readiness result. Handle transitive dependencies, cycles, missing metadata, untracked dependencies, and explicit version constraints without infinite recursion or false certainty.

Let users inspect a dependency tree/graph and explicitly add missing tracked dependencies. Do not silently change collection membership. Required unresolved dependencies block dependency-aware readiness; optional dependencies do not automatically block it. Define aggregate semantics and tests before replacing the MVP summary. This is not a full runtime conflict solver.

### 12.2 `.mrpack` import — high priority

Read a local exported Modrinth pack into a tracking collection. “Upload a pack” in the planning conversation means selecting an import file in the desktop app; it does not require a cloud upload service.

Preview identified projects, duplicates, unresolved entries, and available pack version/loader information. Let the user choose a new or existing collection and set the target version separately from imported current-version information. Use reliable IDs/hashes/provider resolution where available; do not assume every archive entry maps to a Modrinth project.

Do not download referenced mods, install the pack, execute archive content, or modify Minecraft folders. Define safe archive handling and import-conflict behavior when implementing this phase.

### 12.3 Compatibility history

Record meaningful observations with collection, target, loader, timestamp, automatic/effective state, and override context. Support progress over time and individual project changes. Keep different upgrade targets separate; distinguish membership changes from newly available compatibility. Define retention before collecting unbounded snapshots.

### 12.4 Deterministic version-range/family support — medium/high priority

Add deterministic interpretation for explicitly evidenced Minecraft compatibility families and ranges such as `26.x`, `26.3.x`, and `26.3.1–26.3.5`. This is Phase 2 work because these patterns are useful in practice but must not weaken the MVP's conservative compatibility rules.

Only infer compatibility when the evidence source, syntax, boundaries, and loader/distribution applicability are well defined. Do not derive positive compatibility from arbitrary descriptions, changelogs, filenames, or fuzzy text matching. Show the rule and provenance used for any inferred result, and add focused tests for supported Minecraft version formats and boundary cases. Manual overrides remain available when evidence cannot be interpreted deterministically.

### 12.5 Future considerations

Potential later work includes JAR-directory identification with explicit ambiguity handling; tracker collection export/import; user-configured refresh schedules; notifications for meaningful readiness changes; Windows notifications and tray mode; multi-loader collections; CurseForge support; and a separately scoped web application.

Do not implement these merely because the data model could support them. No notification or background service is necessary to satisfy the original manual status-checking workflow.

## 13. Explicit non-goals

The MVP does not:

- Download, install, update, delete, or otherwise manage Minecraft mods or datapacks.
- Launch Minecraft or modify actual modpack/game directories.
- Build, publish, or export playable modpacks.
- Infer the user's installed project versions from disk.
- Prove runtime compatibility, resolve every mod conflict, or test a game instance.
- Treat an absent target release as proof that a project cannot work.
- Infer compatibility through fuzzy text matching, an LLM, filenames, or undocumented version-family assumptions.
- Require Modrinth accounts, authentication, cloud sync, a hosted backend, or telemetry infrastructure.
- Build a web app or web API in preparation for a hypothetical future product.
- Implement dependency analysis, `.mrpack` import, or history before the required MVP is complete.
- Implement CurseForge, arbitrary project types, multi-loader packs, notifications, tray mode, or scheduled refresh as MVP features.

Fetching metadata and project icons is allowed; the prohibition on downloads concerns playable mod/datapack artifacts and game management.

## 14. Acceptance criteria

### 14.1 Product and persistence

| ID | Scenario and required result |
|---|---|
| A01 | Create two collections with separate current/target settings and loaders; rename and reopen them after restarting without data loss. |
| A02 | Add the same Modrinth project to both collections. It appears once per collection, and removing it from one preserves the other. |
| A03 | Add by search and by URL. Duplicate additions are prevented; bad URLs, empty searches, and service failures yield useful feedback. |
| A04 | Change only the target version. Current remains unchanged, stale-context results do not appear under the new target, and old overrides do not leak. |
| A05 | Refresh does not alter current Minecraft version or any actual Minecraft files. |
| A06 | Restart offline. Saved collections, overrides, settings, and dated cached results remain available. |
| A07 | Table search, combined filters, sorting, and visible-column preferences work and preserve their documented settings. Collection totals remain independent of filters. |
| A08 | Details show available metadata and links, clearly distinguish latest overall from selected compatible version, and handle absent optional fields. |
| A09 | For a tracked project, show a modest current-versus-target comparison using the collection’s current Minecraft version and target version so the user can see upgrade movement without opening a complex version browser. |

### 14.2 Compatibility and overrides

Use deterministic test fixtures for these cases; do not depend on changing live project data.

| ID | Fixture | Expected result |
|---|---|---|
| C01 | Stable version explicitly supports target and loader | Stable compatible |
| C02 | Only beta supports target and loader | Beta compatible; counted compatible |
| C03 | Only alpha supports target and loader | Alpha compatible; counted compatible |
| C04 | Stable supports target only on Forge; collection is Fabric | Not detected compatible after a successful complete check |
| C05 | Only Minecraft `26.3-pre1` is listed; target is `26.3` | No automatic positive |
| C06 | Only `26.3` is listed; target is `26.3.1` | No prefix/family inference |
| C07 | Description says `26.3.x`; target is not explicitly listed | No automatic positive; user can override |
| C08 | Older qualifying stable and newer qualifying beta exist | Stable headline; deterministic selected stable candidate |
| C09 | Project-wide metadata contains target and Fabric, but no single candidate has both | No automatic positive |
| C10 | No versions match after a successful complete lookup | Not detected as compatible |
| C11 | Version lookup fails or required evidence is malformed | Unknown; previous evidence preserved and dated |
| C12 | Recognized datapack artifact explicitly supports target in a Fabric collection | Datapack evaluated by its own distribution support; no artificial Fabric requirement |
| C13 | Archived project retains a qualifying version | Compatible state plus archive badge |
| C14 | Apply manual stable/beta/alpha with a note and optional evidence/reference URL | Effective state changes; automatic evidence remains visible; note/reference persist; no fabricated release |
| C15 | Apply manual not-compatible to automatic stable | Effective blocker; automatic stable still visible in details |
| C16 | Refresh while an override is active | Automatic evidence updates; manual choice, note, and evidence/reference URL survive |
| C17 | Change collection, target, or loader | Override applies only to its exact saved scope |
| C18 | Reset override | Automatic status becomes effective again |
| C19 | Ignore a project | Excluded from readiness denominator, still visible and counted as ignored |

### 14.3 Totals, refresh, and responsiveness

| ID | Scenario and required result |
|---|---|
| R01 | Ten tracked projects: four stable, two beta, one alpha, one not detected, one unknown, one ignored. Show 7/9 compatible (about 78%), one ignored, and correct channel totals. |
| R02 | Empty collection or every project ignored. Show no readiness percentage and no false 100% claim. |
| R03 | Refresh a representative 100-project collection. UI remains responsive; progress and completion/failure counts are accurate. |
| R04 | One project's request fails. Other results are saved; the failed project is unknown with last-known evidence and a retry path. |
| R05 | Simulate throttling and transient service failure. Retry/backoff is bounded; cancellation remains usable; there is no request storm. |
| R06 | A project changes slug. Stable ID preserves memberships and overrides; display metadata/links update. |
| R07 | A project becomes unavailable. Its row and local decisions remain, with an availability explanation. |
| R08 | Change target or cancel during refresh. Late results cannot corrupt the active context; completed data remains consistent. |
| R09 | Missing icon/wiki/Discord metadata. Compatibility still evaluates when sufficient version evidence exists. |
| R10 | A local save fails. The UI reports the failure and does not falsely imply durable success. |

## 15. Implementation deliverables and definition of done

When implementation is authorized, deliver:

1. Complete source and a conventional C#/.NET solution implementing the required MVP.
2. A Windows application that has been built and launched, with a publishable executable/distribution and exact publish instructions. A full installer is optional if a usable publish output is supplied; code signing is not required by this specification.
3. Real Modrinth integration, local database persistence, and graceful refresh/error behavior.
4. A README covering prerequisites, build, run, test, publish, data/log locations, and known limitations.
5. A short architecture note explaining framework/storage choices, compatibility rules, and reasonable future web reuse.
6. Focused automated tests for compatibility, overrides, totals, context changes, partial failure, and persistence/migrations. Use controlled provider fixtures so tests do not depend on live compatibility changing.
7. A manual smoke test of the Windows workflow, plus a live API integration check when network access permits. Record actual results and environment limitations; do not claim unperformed checks passed.
8. An optional-to-load sample collection demonstrating the UI. Label synthetic/sample statuses clearly and never present them as current provider facts.

The MVP is done when required acceptance criteria pass, the desktop app runs, restart persistence works, the documented build/publish path is reproducible, and important limitations are documented. Compilation alone is insufficient. Optional and later-phase work must not be used to obscure unfinished required behavior.

## 16. Review notes and decisions intentionally left open

The product requirements are ready for review. The following proposed defaults deserve particular attention:

- Stable-first candidate selection even when a newer beta exists.
- Alpha and beta included in readiness, ignored projects excluded, unknown included but not counted ready.
- Failed checks becoming Unknown while retaining dated last-known evidence.
- Scoped overrides retained for possible return to the exact same target/loader, including their optional notes and evidence/reference URLs.
- Datapacks evaluated by distribution form rather than requiring the collection's mod-loader tag.
- SQLite as the storage implementation; configurable column visibility required, grouping/reordering optional MVP polish.
- Deterministic version-range/family interpretation is Phase 2 medium/high priority, while MVP remains explicit-metadata-or-manual-override only.
- Manual overrides may store an optional evidence/reference URL in addition to a note.

The implementation may choose the app name, Windows UI framework, supported .NET release, minimum supported Windows version, concrete libraries, and packaging method. Document those choices without expanding scope. No final launch prompt or application implementation is part of this document-creation task.
