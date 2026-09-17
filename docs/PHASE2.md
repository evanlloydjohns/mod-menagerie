# Phase 2 behavior

## Dependency readiness

The dashboard continues to measure individual project support. The separate Dependencies window counts a project as ready only when its effective compatibility is positive, an automatic target candidate exists, and that candidate's required dependency chain is satisfied by the selected releases of included tracked projects. Ignored projects are excluded from the denominator, but an ignored required dependency still blocks its dependents. An empty collection shows 0/0, without a percentage or a claim of readiness.

Required untracked projects block until explicitly added and refreshed. Required exact-version references must match the dependency project's selected candidate. The analyzer does not select an alternative combination even if one might work. Incompatible relationships block when the referenced project/version is included. Missing or malformed metadata, cycles, and unidentified relationship types remain blocked/unknown. Optional and embedded relationships are informational leaves. A manually compatible project without an automatic candidate cannot establish dependency readiness.

Only selected target releases are traversed, never the project-wide union of dependencies. Analysis is bounded to 32 levels and approximately 6,000 displayed lines. Individual retrieval errors stay visible; cancellation keeps the previous dated report. Context/membership/decision/release changes invalidate the persisted report. Cached reports can be reviewed offline; resolving missing release/project metadata needs the network. Adding a missing project never happens automatically.

Uses official [version metadata](https://docs.modrinth.com/api/operations/getversion/); the [project dependency union](https://docs.modrinth.com/api/operations/getdependencies/) is deliberately not used. Runtime conflicts, installed versions, platform environments, and alternative-release solving are not verified.

## Local pack import

Supports Minecraft format-version-1 archives from the [official mrpack specification](https://support.modrinth.com/en/articles/8802351-modrinth-modpack-format-mrpack). The app opens only the root `modrinth.index.json`, checks the Minecraft version and exactly one supported loader, and resolves SHA-512 (preferred) or SHA-1 hashes through the official [hash endpoint](https://docs.modrinth.com/api/operations/versionfromhash/). A response must uniquely identify one release and confirm the requested hash. Ambiguous form/loader metadata remains unresolved; filenames never identify projects.

Safety limits: 1 GiB archive, 20,000 ZIP entries, 4 MiB expanded index, JSON depth 32, and 2,000 indexed files. Duplicate root indexes and malformed pack metadata are rejected. Unsafe paths, missing hashes and unresolved entries are shown and skipped. No archive content is extracted, no referenced download URL is followed, and no mod file is downloaded or executed. Additional archive entries such as overrides are warned about but are not identified. Client/server environment declarations are displayed, not silently used to omit files.

Identification is a cancelable preview. Archive duplicates are unchecked; already tracked destination projects are labeled and skipped. Import commits new memberships atomically. An existing destination must retain its current version and loader; target, distribution choices and overrides stay unchanged. A new destination requires a separate target entry. Import does not infer installed versions or target compatibility; refresh afterward. The last import report is retained locally as a preference record. Unresolved and override entries are outside the tracked readiness denominator, so inspect the preview before treating the collection as a full representation of the pack.

## Observations and retention

Refresh (including cancellation), collection edits, membership changes, manual decisions, import and range-rule changes record snapshots. Each preserves current/target/loader, timestamp, membership, automatic/effective status, selected release and the scoped manual decision including source and modification time. The history viewer shows progress and per-project changes relative to the preceding observation in the same context; added/removed memberships are counted separately.

Keep at most 200 observations per collection and 365 days. Pruning occurs on new observations; older entries are also excluded when reading. Deleting a collection removes its history. This is a bounded observation log, not an audit system, and does not backfill earlier activity.

## Explicit range evidence

Rules are entered deliberately from a user-reviewed authoritative source, bound to collection, project, exact release, collection loader and distribution. The release must still explicitly list the applicable loader (`datapack` for datapacks). The engine never parses descriptions or version titles for support.

Supported syntax: one to four numeric components (each at most six digits, without leading zeros), a final `.x` family, or inclusive endpoints separated by `-` or `–`. Families require at least one additional component: `26.x` matches `26.3` and `26.3.1`, but not `26`; `26.3.x` matches `26.3.1`, but not `26.3`. Ranges require equal component counts in both endpoints and target; `26.3.1–26.3.5` includes both endpoints. Snapshots, prereleases, wildcards other than `.x`, reversed ranges and mixed-length endpoints do not match.

An HTTPS evidence URL and explanatory note are mandatory. URL contents are not fetched or interpreted, so verifying the claim remains the user's responsibility. Exact provider evidence is preferred when present. Otherwise the rule, URL and note appear in the automatic evidence. Stable/beta/alpha selection and manual override precedence are unchanged. Removing a membership removes its range rules; unrelated collections remain intact.
