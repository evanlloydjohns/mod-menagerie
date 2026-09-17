# Architecture

## Desktop and boundaries

WPF provides native Windows controls, keyboard navigation, a mature DataGrid, straightforward .NET publishing, and familiar C#/XAML. .NET 10 is the installed supported LTS generation. The UI is intentionally conventional: XAML for the main layout and thin event-driven presentation code for desktop interactions. Immutable domain rows form the grid's binding model. Dialogs and process/clipboard calls remain in Desktop; compatibility and refresh are tested without opening a window.

- **Domain:** immutable records, exact-match compatibility engine, status labels, readiness arithmetic. No desktop, network, or database dependencies.
- **Application:** provider/store contracts, URL validation, collection row composition, scoped override validation, and refresh orchestration. An immutable Pack snapshot determines each refresh scope. Concurrent refreshes of one pack are rejected; cancellation retains completed writes.
- **Infrastructure:** Modrinth mapping and HTTP behavior; SQLite implementation. Metadata DTO interpretation stays here. Stable IDs are keys; slugs are display/link metadata.
- **Desktop:** composition root, per-user storage location, single-instance guard, asynchronous workflows, table controls, details, links, and error presentation. Refresh I/O, mapping, evaluation, and writes run off the dispatcher; progress returns to it.
- **Tests:** deterministic fixtures plus an explicitly enabled live API check. SQLite tests use isolated temporary databases, including an aborting trigger to prove failed writes roll back.

This avoids generic repositories, service buses, web endpoints, hosted workers, accounts, and speculative provider frameworks. A future web presentation can reuse Domain/Application and the Modrinth client, supply different persistence and user scoping, and replace desktop-specific interactions. No web hosting, tenancy, or synchronization is implemented.

## Modrinth contract

Verified against the official documentation and live API on 2026-09-16:

- [API overview](https://docs.modrinth.com/api/): production base `https://api.modrinth.com/v2/`, public reads, stable IDs, identifying User-Agent, response-directed quotas.
- [Search](https://docs.modrinth.com/api/operations/searchprojects/): offset/limit pagination and mod project facet. Datapacks in v2 are represented as mod projects with explicit `datapack` loader distribution.
- [Project](https://docs.modrinth.com/api/operations/getproject/): metadata, links, lifecycle, license, supported tags; project members are fetched for authors/roles, with optional-field failure isolated.
- [Versions](https://docs.modrinth.com/api/operations/getprojectversions/): unfiltered full list with `include_changelog=false`. This endpoint has no pagination parameter. Structured version ID, project ID, timestamp, publication status, game-version array, loader array, and channel are validated.
- `/tag/game_version` and `/tag/loader` supply cached selectors. Only full Minecraft releases populate the default list; typed exact identifiers are allowed.

No arbitrary URL is fetched from the add flow. Main Modrinth URLs are parsed into a slug, then resolved through the fixed API base. No credentials are needed. No artifact download URLs are used. A single HTTP gate bounds all API traffic; rate-limit remaining/reset and Retry-After are honored. Throttling, server errors, timeouts and transport failures have at most three attempts, with cancelable waits. Permanent HTTP errors are not retried. Diagnostics are logged locally.

## Compatibility and context

The same release must explicitly contain the exact target and required loader. Datapacks require `datapack`, independent of the collection's mod loader. Stable (`release`) wins over beta, then alpha; within a channel choose descending publication time and ordinal ascending ID. Unrecognized or incomplete required evidence yields Unknown conservatively. A complete successful empty match yields Not detected, not proof of incompatibility. Lifecycle is orthogonal.

Overrides use `(pack, project, target, loader, form)`, retain local modification time, note, and optional reference. Current Minecraft version is not part of override scope because it does not change the target's support. A target/loader change never reuses another scope's evaluation/override; complete cached versions can be reevaluated for the new context. Returning to an exact previous scope restores its stored override. Automatic evidence remains separate from the effective result, and no manual decision fabricates a release.

Refresh saves each project independently in a transaction. Failure stores Unknown and preserves last successful evidence. Removed memberships are checked before save so late results cannot resurrect them. Old-context responses write only the captured old scope. The latest automatic outcome per scope is retained, not a time-series history. Shared metadata/version caches are reusable across modpacks.

Readiness is `(Stable + Beta + Alpha) / (Tracked - Ignored)`. Unknown remains in the denominator. Zero included projects means no percentage. Summary counts use all memberships, independently of the table view.

## Persistence and choices

SQLite tables: packs, projects, memberships, versions, evaluations, overrides, preferences. Identity, many-to-many membership, uniqueness, and cascading membership/override cleanup are relational; descriptive payloads are JSON. This keeps provider metadata flexible without an ORM or dozens of join tables. User-authored and cache records stay separate. The initial migration creates schema version 1 transactionally; future versions must add explicit migrations. Newer schemas are refused rather than destroyed. Foreign keys protect local state, and exceptions from durable writes are never reported as success.

Metadata is reusable but can become stale; the app displays cached dates and retrieval outcomes rather than inventing an expiry. Current-version evidence uses the same conservative engine on the dated cache. The modest two-column comparison does not claim knowledge of installed artifact versions.

Phase 2 can add dependency evaluation from the selected target candidate, `.mrpack` metadata import, context-specific history, and rigorously tested structured range rules. None is implemented in this MVP.
