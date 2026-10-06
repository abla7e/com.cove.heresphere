# Contributing to the HereSphere extension

This is the developer/architecture reference for `com.cove.heresphere`. The [README](./README.md)
is for end users; this file is for anyone (human or AI agent) building, debugging, or extending
the extension itself.

## 1. What this project is

A [Cove](https://github.com/yourcove/cove) extension that implements the **HereSphere Web API v1**
(the protocol the HereSphere VR video player uses to browse a remote media library) on top of
Cove's data model, auth, and media pipeline. It is a single ASP.NET Core minimal-API extension
assembly (`HereSphereExtension.dll`) loaded into a running Cove host process — there is no separate
server, database, or process to run.

Read `HERESPHERE_API_REFERENCE.md` / `HERESPHERE_API_QUICK_REFERENCE.md` (wherever your copy of
that reference material lives) before changing any response shape — it is the authoritative,
independently-verified description of the protocol this extension must speak correctly. When a
Cove-side convention and a HereSphere-side convention conflict, HereSphere's documented behavior
wins for anything in the JSON responses; Cove's own conventions (auth, EF Core patterns, DI) win
for everything else.

## 2. Repository layout

```
com.cove.heresphere/
├── README.md                 — end-user facing (features, setup, troubleshooting)
├── CONTRIBUTING.md           — this file (architecture, dev workflow)
├── CHANGELOG.md              — Keep a Changelog format, semver
├── Directory.Build.props     — shared MSBuild props (TFM, local-vs-package Cove.Sdk switch)
├── com.cove.heresphere.slnx  — solution file
└── src/HereSphereExtension/
    ├── extension.json              — Cove extension manifest (id, version, entryDll, jsBundle)
    ├── HereSphereExtension.csproj
    ├── HereSphereExtension.cs      — IApiExtension entry point: route registration, catalog, auth
    ├── HereSphereEndpoints.cs      — scan, per-video data/write-back, media/thumbnail/preview streaming
    ├── HereSphereAuth.cs           — credential/token resolution (ResolveAccessAsync)
    ├── HereSphereData.cs           — DTO builders: media entry, video data, tags, resolution tiers, groups
    ├── HereSphereVrStreaming.cs    — VR descriptor mapping, title/URL helpers, file streaming
    ├── HereSphereModels.cs         — request + catalog/auth/scan DTOs
    ├── HereSphereVideoModels.cs    — media-entry + video-data DTOs
    ├── HereSphereSettings.cs       — settings model, tag categories, extension-store load/save
    ├── HereSphereSettingsEndpoints.cs — /api/heresphere/settings and /api/heresphere/tokens
    ├── HereSphereDebug.cs          — GET /api/heresphere/debug (read-only diagnostics) + UI manifest
    ├── HereSphereLogging.cs        — Trace-level request/response/write-back logging helpers
    ├── CoveGeneratedAssets.cs      — mirrors Cove's on-disk generated-asset paths
    └── ui.mjs                      — React bundle for the Settings → HereSphere debug page
```

All `.cs` files in `src/HereSphereExtension/` (except `CoveGeneratedAssets.cs` and the plain DTO
records in `HereSphereDebug.cs`) are `partial class HereSphereExtension` — there is exactly one
class, split across files by concern. When adding a new endpoint or helper, pick the file whose
concern it matches rather than creating a new file for one or two methods.

## 3. Build & local development

### Environment

- .NET 10 SDK. In sandboxed/bwrap environments the SDK is often mounted at
  `/run/host/usr/share/dotnet/dotnet` rather than the usual `/usr/share/dotnet/dotnet` — try that
  path first if `dotnet` isn't found.
- This extension is normally developed **beside a `cove` checkout** (the two repos as sibling
  directories, e.g. a workspace root containing both `cove/` and `com.cove.heresphere/`).
  `Directory.Build.props` auto-detects a sibling `../cove/src/Cove.Sdk/Cove.Sdk.csproj` and sets
  `UseLocalCoveSdk=true`, which makes the csproj use a `ProjectReference` to the local
  `Cove.Sdk` project instead of a NuGet package — so you can iterate against unreleased Cove
  changes without publishing a package first.

### Build

```bash
cd com.cove.heresphere
dotnet build -c Release
```

This resolves the local `Cove.Sdk`/`Cove.Core`/`Cove.Plugins` projects (compile-time only —
`Private=false, ExcludeAssets=runtime`, since the running Cove host provides its own copies of
those assemblies at runtime) and produces `src/HereSphereExtension/bin/Release/net10.0/`.

Standalone (no sibling `cove` checkout — e.g. CI building just this repo against a published
package):

```bash
dotnet build -p:UseLocalCoveSdk=false -p:CoveSdkVersion=1.0.0
```

### Packaging a release zip

Cove loads an extension from a flat directory containing `extension.json` + the entry DLL (+
`.deps.json`, `.runtimeconfig.json`, and `ui.mjs`) at the archive root — **no subfolder**. A local
*ProjectReference* build does **not** run `Cove.Sdk`'s `buildTransitive/Cove.Sdk.targets`
host-assembly-stripping step (that only fires for the `PackageReference` path), so a `dotnet
build`/`publish` against the local SDK leaves `Cove.Core.dll`/`Cove.Plugins.dll`/`Cove.Sdk.dll`
sitting in the output — those must **not** ship in the zip (the host already has its own loaded
copies; shipping stale copies risks a type-identity mismatch at load time). The release workflow:

```bash
cd com.cove.heresphere
dotnet publish src/HereSphereExtension/HereSphereExtension.csproj -c Release \
  -o artifacts/extension -p:UseLocalCoveSdk=true

# Local-SDK publish leaks the host-provided assemblies — strip them before zipping.
rm -f artifacts/extension/Cove*.dll artifacts/extension/*.pdb

cd artifacts/extension
zip -r ../../../com.cove.heresphere-<version>.zip .
```

Keep `HereSphereExtension.deps.json` in the zip — it lists `Cove.Core`/`Cove.Plugins` as
host-provided dependencies that the running host resolves from its own already-loaded copies; it
does **not** cause the host assemblies to be re-shipped.

Verify a packaged zip is flat (no top-level folder) and contains exactly: `extension.json`,
`HereSphereExtension.dll`, `HereSphereExtension.deps.json`, `HereSphereExtension.runtimeconfig.json`,
`ui.mjs`. No `Cove*.dll`, no `.pdb`.

### Versioning

Bump `version` in `src/HereSphereExtension/extension.json` (semver) for every release, and add a
dated entry to `CHANGELOG.md` under `## [Unreleased]` → a new `## [x.y.z] - YYYY-MM-DD` heading
following the existing Keep a Changelog style in that file. The zip filename convention is
`com.cove.heresphere-<version>.zip`.

### Testing without a live instance

This extension currently has **no automated test project**. Validate changes by:
1. `dotnet build -c Release` — must be 0 warnings, 0 errors.
2. Code review against `HERESPHERE_API_REFERENCE.md` for any JSON shape you touched.
3. If you have access to a non-production Cove instance, package and load the extension there and
   exercise the real HereSphere app or `curl`/Postman against `/heresphere`, `/heresphere/scan`,
   and `/heresphere/video/{id}`.
4. **Never test against a live/production Cove instance or its real user data** unless explicitly
   told to by whoever owns that instance.

## 4. How this extension integrates with Cove

### Extension surface

`HereSphereExtension` is a `CoveExtensionBase` (for UI-manifest/page registration — the debug page)
implementing `IApiExtension` (for `MapEndpoints`), from `Cove.Sdk`. `extension.json` declares
`entryDll` + `jsBundle`; the host loads the DLL, instantiates the class via DI, calls
`ConfigureServices` (a no-op here — no extension-owned services are needed), then `MapEndpoints`.

Every endpoint this extension registers under `/heresphere/*` calls `.AllowCoveAnonymous()`. This
is deliberate, **not** an oversight: HereSphere's own auth model (username/password or an
`auth-token` header, resolved per-request by this extension's own `ResolveAccessAsync`) must run
*instead of* Cove's standard pipeline auth for these routes, because a HereSphere client is not a
browser carrying Cove session cookies. `AllowCoveAnonymous()` only waives Cove's own
authorization — it does not mean "insecure"; every `/heresphere/*` handler independently resolves
and gates on a Cove identity before returning any real data. The one exception is
`/api/heresphere/debug`, which uses `.AllowWithoutCovePermission()` because it is a *browser-only*
diagnostics endpoint meant to run under a real, already-authenticated Cove session (see §6).

### Service resolution

The extension does not register or inject its own services. Every handler resolves host-registered
Cove services from `http.RequestServices` at request time (`GetRequiredService<DbContext>()`,
`GetRequiredService<ITokenService>()`, `GetRequiredService<IUserEngagementService>()`, etc.) —
this is the existing convention in this codebase and should stay that way; do not add a
constructor-injected service to `HereSphereExtension` itself.

### Database access pattern

Every video/group/tag query uses `.IgnoreQueryFilters().AsNoTracking()`. Both are required:
- `IgnoreQueryFilters()` — Cove's `DbContext` applies per-request authorization query filters keyed
  to `ICurrentPrincipalAccessor.Current`. HereSphere's own principal (resolved from
  username/password or an `auth-token`, not the ambient Cove session) is not necessarily the
  request's ambient Cove principal, so the filtered queries would silently return nothing (or the
  wrong user's data) without this.
- `AsNoTracking()` — every read here is one-shot; nothing is mutated through these entities
  directly (writes go through `IUserEngagementService`, never direct EF mutation of `Video`/`Tag`).

### Authentication (`HereSphereAuth.cs`)

`ResolveAccessAsync` is the single source of truth for "who is this HereSphere request, and what
HSP `access` level do they get". It is called by every data-returning endpoint
(`CatalogAsync`/`AuthAsync`/`ScanAsync`/`VideoAsync`, and the debug endpoint for introspection) and
resolves, in priority order:
1. An explicit `auth-token` header — HereSphere's steady-state path once it has logged in once.
   Resolved via `ITokenService.ResolveAsync`. A present-but-invalid token is a **bad login**
   (`access: -1`), not a guest.
2. The ambient Cove session principal already resolved by Cove's own
   `CurrentPrincipalMiddleware` — i.e. a standard `Authorization` header, a `?access_token=` query
   token, **or the `cove_access_token` browser cookie**. This exists so a logged-in Cove *browser*
   session (no HereSphere `auth-token` at all) still resolves to a real user rather than falling to
   guest — important for the debug page and for any same-origin fetch from Cove's own UI.
3. `username`/`password` in the JSON request body — verified against `IUserService`, then a fresh,
   unscoped, non-expiring `cove_pat_*` API token is issued (naming convention: a token named
   exactly `"HereSphere"`) and any *previous* token with that same name for that user is revoked
   first, so each user holds exactly one live HereSphere token at a time. This token is returned
   raw (no `Bearer` prefix) as `auth-token` in the response, which is what HereSphere stores and
   resends on every later request.
4. No credentials at all → guest (`access: 0`).

Guest and bad-login both result in **empty** catalog/scan responses (no `media`, no `library`,
empty `scanData`) — this is an explicit design decision (not a bug), because the HereSphere client
handles an empty library gracefully and Cove data must never be exposed to an unauthenticated
caller.

## 5. The tag-category design (read this before touching `BuildTags`)

HereSphere derives a tag's display *category* from the substring before the first `:` in its
`name` field — a tag with no colon lands in an "Uncategorized" bucket, and HereSphere sorts
categories alphabetically/numerically (ASCII: digits sort before letters). Since 0.6.0 **every**
category is numbered, and the numbers come from the order configured on the settings page
(`HereSphereSettings.TagCategoryOrder`, see §5a). `BuildTags` walks that order and asks
`HereSphereRequestSettings.Prefix(category)` for each `"N. Label"` prefix. Default order:

| Key | Default prefix | Source |
|---|---|---|
| `tag` | `1. Tag:` | `Video.VideoTags[].Tag.Name` |
| `group` | `2. Group:` | Static group membership + 3 built-in per-user dynamic groups |
| `performer` | `3. Performer:` | `Video.VideoPerformers[].Performer.Name` |
| `studio` | `4. Studio:` | `Video.Studio.Name` (query includes `Studio`) |
| `segment` | `5. Segment:` | Cove `Segment` rows, as timestamped tags (`start`/`end` ms, `track` 0) |
| `resolution` | `6. Resolution:` | `ResolveMajorResolutionTier` |
| `rating` | `7. Rating:` | The current viewer's Cove rating, HSP-scaled |
| `vr` | `8. VR:` | `Video.IsVr` |

To add a category: add a key to `HereSphereTagCategories` (constant, `DefaultOrder`, `Label`), a
`case` in `AddCategoryTags`, and a row in this table and in the README's table. `Normalize()`
appends new keys to saved orders automatically, so existing installs pick them up at the end.

**Do not use the stash-vr convention** (`#:`, `%:`, `@:` single-character prefixes). It was
evaluated and rejected as confusing in favor of descriptive, numbered category names.

### Segment markers (`BuildSegmentMarkerMapAsync`)

Segments are batch-loaded once per request (`IgnoreQueryFilters().AsNoTracking()`, including
`Tag`) and turned into `SegmentMarker`s: label = `Title`, falling back to `Tag.Name` (unlabeled
segments are skipped); seconds become milliseconds; a missing `EndSec` becomes `end: 0`
("through the end" in HereSphere). `SegmentSource` setting: `user` filters on
`SourceKey == "user"` (hand-made, the same filter Cove's own segment endpoints use for "user"),
`all` keeps AI/automatic segments too, `none` skips the query. For a clip
(`ParentVideoId != null`) the clip's own segments are used as-is, and the parent's segments that
overlap `ClipStartSec..ClipEndSec` are shifted onto the clip's timeline and trimmed. I couldn't
confirm from Cove's code which of the two Cove uses for clips, so both are handled.

### Subtitles (`BuildSubtitles` / `SubtitleAsync`)

`VideoFile.Captions` (`VideoCaption`: language code, `srt`/`vtt`, sidecar filename) become the
video-data `subtitles[]` field (single-video response only; the query includes
`Files.Captions`). URLs are `/heresphere/subtitle/{captionId}.{srt|vtt}`: the extension is part of
the URL because HereSphere picks its subtitle parser from it. `SubtitleAsync` mirrors Cove's
`StreamController.GetCaption` (sidecar resolved next to the file's `Path`), additionally rejects a
filename containing directory parts, and requires the URL extension to match the caption type.

Tag *values* use the Cove entity's real display name (`Tag.Name`, `Performer.Name`, `Group.Name`)
— **never** `Tag.NamespaceKey` (that field is a lower-cased, case-insensitive-matching key, not a
display string; using it was the root cause of a past bug where tags showed up lowercased in
HereSphere regardless of how the user capitalized them in Cove).

### Groups as tags vs. groups as library sections

The same group membership data feeds two different parts of the response:
- **`library[]` sections** in the catalog (`BuildGroupSectionsAsync`) — one named shelf per group,
  containing only the subset of its videos present in the currently-loaded catalog page.
- **`2. Group:<name>` tags** on each video (`BuildVideoGroupMapAsync` / `BuildTags`) — so the same
  group membership is visible/searchable from the per-video tag list, not just the shelf view.

Both draw from the same two sources, and both must stay in sync if you change what counts as a
"group" for HereSphere purposes:
1. **Every `Group` with `Kind == GroupKind.Static`**, regardless of its `ShowInVideoLists` flag.
   That flag only controls whether a group appears in **Cove's own** browsing UI — it has nothing
   to do with HereSphere, and gating on it was a past bug (a group like a personal "Watchlist"
   that was never explicitly flagged `ShowInVideoLists` was silently invisible to HereSphere even
   though it had real members). Dynamic (`GroupKind.Dynamic`) groups driven by Cove's generic
   filter engine (`QuerySourceKey == "filter"`) are **not** resolved here — that would require
   re-implementing Cove's filter-query engine inside the extension (it lives in `Cove.Api`, which
   this extension cannot reference); only the three built-in dynamic sources below are supported.
2. **The three built-in per-user dynamic groups** Cove derives from engagement data — resolved
   directly against the underlying tables (`BuildDynamicGroupVideoMapAsync`) rather than through
   Cove's `DynamicGroupResolver`/`IDynamicGroupSource` (those live in `Cove.Api.Services`, not
   accessible to an extension that only references `Cove.Core`/`Cove.Plugins`):
   - **Save for Later** — `UserBookmark` rows for the current user.
   - **Watch History** — `UserEntityAffinity` rows with `LastConsumedAt != null`, most recent first.
   - **Continue Watching** — `UserEntityAffinity` rows with a positive `LastPositionSec`,
     `CompleteCount == 0`, and (when video duration is known) not within 5% of the end — mirrors
     Cove's own `ContinueWatchingDynamicGroupSource` criteria in `Cove.Api/Services/DynamicGroups.cs`;
     keep these two in sync if Cove's own criteria changes.

If Cove ever exposes per-user ownership on `Group` itself (it does not today — `Group` is a
globally shared entity with no owner column), re-evaluate whether "per-user groups" should instead
mean "groups owned by this user" rather than "static groups + built-in dynamic groups".

## 5a. Settings and the settings page (`HereSphereSettings.cs`, `HereSphereSettingsEndpoints.cs`)

- **Storage:** the extension implements `IStatefulExtension`; the host calls `SetStore` with an
  `IExtensionStore` (Cove's `extension_data` table, scoped to this extension). Settings are one JSON
  value under the key `settings`, cached in memory after the first read and replaced on save. A
  missing or unreadable value means defaults, so HereSphere requests never fail on configuration.
- **Shape:** `tagCategoryOrder` (list of category keys), `segmentSource` (`user`/`all`/`none`),
  `subtitlesEnabled`. `Normalize()` drops unknown keys and duplicates, appends missing keys, and
  coerces an unknown segment source to `user`. Always go through it.
- **Endpoints** (all Cove-session, called by `ui.mjs` via `extensionFetch`):
  - `GET /api/heresphere/settings`: current settings, category labels, `canEdit`.
  - `PUT /api/heresphere/settings`: requires `extensions.configure` (checked in the handler so the
    page gets a readable 403). Settings are extension-wide, not per user.
  - `GET /api/heresphere/tokens` / `DELETE /api/heresphere/tokens/{id}`: the signed-in user's own
    active API tokens, and revoking one. The handler checks the id is in the caller's own list, and
    Cove's `RevokeApiTokenAsync` re-checks ownership (it only updates rows with the actor's
    `UserId`) and writes the audit log entry.
- **UI:** `ui.mjs` exports `HereSphereSettings` (settings card + token card with a × per token) and
  `HereSphereDebug`. Both are registered as sections on the `heresphere` settings tab, ordered
  10 and 100.

## 6. Resolution tiering (`ResolveMajorResolutionTier` / `ResolveResolutionAxis`)

Goal: collapse a video's exact pixel dimensions into one of a fixed set of "major" resolution tiers
(`240`…`8640`) so the HereSphere tag list isn't cluttered with near-duplicate tags like `1082p`,
`1078p`, `1074p` for files that are all "basically 1080p".

- **Axis selection (`ResolveResolutionAxis`):** uses the **shorter** of the primary file's width
  and height. Conventional resolution names (`1080p`, `4K`, …) refer to this shorter axis in both
  orientations — a 1920×1080 landscape file and a 1080×1920 portrait file are both "1080p" — so
  portrait content is correctly measured by width without any orientation-specific branching.
  Falls back to the denormalized `Video.MaxHeight` only when no file has usable dimension data.
- **Tier matching (`ResolveMajorResolutionTier`):** walks the tier ladder from the top down and
  picks the **first (highest) tier the axis reaches at least `ResolutionTierThreshold` (90%) of**.
  This is a **strict floor, not "nearest tier"** — deliberately different from the stash-vr
  extension's "closest" behavior, which was evaluated and rejected: at 50% of 1080 (540px),
  "closest" would wrongly suggest 1080p is still "close enough", when perceptually it's a
  materially lower resolution. A value below the lowest tier still reports the lowest tier (there
  is no "below everything" category — don't add one without confirming that's actually wanted).

If you change the tier ladder or threshold, update the worked examples in the README's
"Resolution tiers" section to match, and sanity-check a few boundary cases (just above/below each
`tier * threshold` cutoff) — it's easy to get an off-by-one on which tier a boundary value lands in.

## 7. Ratings and favorites

- **Scale:** Cove stores ratings as an int 0–100 (`Rating.Value`); HereSphere uses a decimal 0–5.
  Conversion is exactly `cove = round(hsp * 20)` / `hsp = cove / 20.0` (`ToCoveRating`/`ToHspRating`
  in `HereSphereData.cs`). Do not introduce a different scale factor anywhere else in the codebase.
- **Read path:** `IUserEngagementService.GetVideoSnapshotsAsync`/`GetSnapshotAsync` return a
  `UserEngagementSnapshot` scoped to the *resolved HereSphere principal* (not necessarily the
  ambient Cove session) — always resolve the principal via `ResolveAccessAsync` first.
- **Write-back path (`VideoAsync` in `HereSphereEndpoints.cs`):** a POST to a video-data URL may
  carry `rating` and/or `isFavorite`. Per the HereSphere spec, these fields are **absent** on a
  routine playback POST (which only sends `{username, password, needsMediaSource}`) — so write-back
  only fires when the field is actually present in the body, and the response is always rebuilt
  from the fresh snapshot afterward so the client sees the authoritative, just-persisted state (no
  "optimistic echo" of what was POSTed). `canWrite` (and therefore `writeRating`/`writeFavorite` in
  the response) is true only for a resolved principal with a Cove `UserId` — guests can never
  write.
- **If ratings/favorites don't show up as stars/hearts in the HereSphere app despite correct JSON**
  (verified independently, e.g. via `curl` or the debug page): this has been root-caused before as
  a **client-side** HereSphere setting, not a server bug — HereSphere has its own local
  per-video cache plus separate "API overwrite" toggles for favorite/rating/tags (§19.1 of the API
  reference) that must be enabled before server data is allowed to replace locally-cached state.
  Verify the raw JSON is correct first (it almost certainly already is) before assuming a server
  regression.

## 8. Observability (`HereSphereLogging.cs`)

All four data-returning endpoints emit structured **Trace**-level log lines (request signals
present, resolved auth source/principal, response summary, write-back detail) through
`ILogger<HereSphereExtension>`, resolved from `http.RequestServices` — this automatically rides
Cove's own `ILoggerFactory`/Serilog pipeline, so it turns on/off exactly when Cove's own trace
logging does (Settings → System → "Start trace logging", or a persisted `Cove:LogLevel: Trace`).
Every helper in `HereSphereLogging.cs` starts with `if (!logger.IsEnabled(LogLevel.Trace)) return;`
— preserve that guard on any new trace helper so there is zero cost (no header/cookie inspection,
no string formatting) when trace logging is off. Use these logs as the first troubleshooting step
for "auth resolves differently than expected" reports (e.g. a clicked catalog link vs. a manually
entered URL behaving differently) — compare the `HSP IN`/`HSP AUTH` lines between the two requests.

## 9. VR metadata (`HereSphereVrStreaming.cs`)

`BuildVr` delegates to Cove's own `VrDescriptorDetector.Resolve` (from `Cove.Core.Helpers`) — the
*same* detector Cove's own UI uses — passing `Video.IsVr`, the explicit `VrProjection`/
`VrFieldOfView`/`VrStereoMode` columns (nullable; a null value means "not set", so the detector
falls back to filename/dimension heuristics), and the primary file's path/width/height. Do not
reimplement VR-format detection independently in this extension; if Cove's detector gains a new
projection/stereo value, map it here rather than guessing.

## 10. Endpoint reference

All routes are registered at the application root (Cove extension routing is literal — no
`/api/ext/<id>` prefix):

| Route | Method(s) | Purpose |
|---|---|---|
| `/heresphere` | `GET`, `POST` | Catalog: `access`, `auth-token`, `library[]`, inlined `media[]`. |
| `/heresphere/auth` | `POST` | Exchange `username`/`password` for an `auth-token`. |
| `/heresphere/video/{id}` | `GET`, `POST` | Video data. `POST` also carries rating/favorite write-back. `?file={fileId}` streams that file instead. |
| `/heresphere/media/{id}` | `GET` | Stream a video file (primary by default, or `?file={fileId}`). |
| `/heresphere/thumbnail/{id}` | `GET` | The image Cove's UI would show for this video. |
| `/heresphere/preview/{id}` | `GET` | The generated hover-preview mp4 (byte-range). |
| `/heresphere/scan` | `GET`, `POST` | Bulk scan metadata (`scanData[]`). |
| `/heresphere/subtitle/{captionId}.{srt\|vtt}` | `GET` | A Cove sidecar caption file (see §5). |
| `/api/heresphere/debug` | `GET` | Browser-only, Cove-session-authenticated diagnostics (see §4, §6). |
| `/api/heresphere/settings` | `GET`, `PUT` | Settings page: read / save settings (PUT needs `extensions.configure`). See §5a. |
| `/api/heresphere/tokens` | `GET` | Settings page: the signed-in user's active API tokens. |
| `/api/heresphere/tokens/{id}` | `DELETE` | Settings page: revoke one of the signed-in user's tokens. |

Every `/heresphere/*` response carries `HereSphere-JSON-Version: 1`. Durations are milliseconds;
dates are `YYYY-MM-DD`.

## 11. Known gaps / potential future features

Cross-referenced against `HERESPHERE_API_REFERENCE.md` and Cove's own data model. None of these
are started; this is a backlog, not a promise.

- **`subtitles[]`.** Cove *does* model video captions (`VideoCaption`/`VideoFile.Captions`, served
  today at `Cove.Api`'s `GET /api/stream/video/{id}/caption/{captionId}`) but this extension does
  not yet expose them as HereSphere `subtitles[]` entries. Likely the most immediately achievable
  gap to close — the data already exists.
- **`scripts[]` (funscript/interactive-script sync).** Cove has no interactive-script/funscript
  entity or storage today — nothing to map this field to yet without a new Cove feature first.
- **`eventServer` (playback-event receiver).** Could feed Cove's `PlaybackSession`/
  `RecordPlaybackIntervalsAsync` (resume position, watch time, play count) from real-time HereSphere
  playback events instead of relying only on rating/favorite write-back. Nontrivial: needs a new
  endpoint accepting HereSphere's playback-event POSTs and a mapping from HereSphere's `event`
  codes to Cove's engagement API.
- **`hspArray`/`writeHSP` (native `.hsp` profile read/write).** Cove has no concept of a HereSphere
  binary HSP profile; this would be an entirely new persisted blob per video with no current Cove
  analog, and the HSP binary layout (`HERESPHERE_API_REFERENCE.md` §20–22) is nontrivial to
  implement correctly. Lowest priority of this list.
- **Timestamped/tracked tags** (`tags[].start`/`end`/`track`) for scene markers — Cove's `Segment`
  entity (chapter/marker-like, with `StartSec`/`EndSec`/`Tag`) is a plausible source; not wired up.
- **`comments`/`favorites` (aggregate, cross-user counts).** Deliberately omitted per prior user
  directive — Cove has no per-video "global favorite count" concept exposed anywhere else, and
  per-user `isFavorite` already covers the single-user case this extension targets.
- **Arbitrary Dynamic/Filter groups as HereSphere groups.** See §5 — blocked on `Cove.Api`'s filter
  engine not being reachable from an extension; would need either a new `Cove.Core`/`Cove.Sdk`
  surface exposing filter resolution, or duplicating that logic here.
- **Playback events → Cove watch history** (`eventServer`). HereSphere can POST open/play/pause/
  close events with the playback position; feeding them into
  `IUserEngagementService.RecordPlaybackIntervalsAsync` would make VR viewing update Watch History,
  Continue Watching and resume position. Not implemented yet: whether HereSphere sends its
  `auth-token` with those events is unverified (capture a trace first).

## 12. Instructions for contributors (human or AI agent)

These apply regardless of who/what is making the change:

1. **Read `HERESPHERE_API_REFERENCE.md` (or the quick reference) before touching any JSON field.**
   Guessing at HereSphere's protocol has caused real bugs here before (lowercase tag names, missing
   groups, an inverted "closest tier" resolution rule). If the reference doesn't answer the
   question, say so explicitly rather than assuming.
2. **Never test against a live/production Cove instance.** Build and reason locally; if you need
   to validate against a real server, confirm with whoever owns that instance first and treat it
   as read-only unless told otherwise.
3. **Keep the partial-class-per-concern file layout** (§2). Don't create `HereSphereExtension2.cs`
   or similar; find the file whose existing responsibility matches your change.
4. **Match the existing query pattern** for any new video/tag/group query:
   `.IgnoreQueryFilters().AsNoTracking()`, and resolve services via
   `http.RequestServices.GetRequiredService<T>()` inside the handler, not through a constructor.
5. **Build before calling anything done:** `dotnet build -c Release` from `com.cove.heresphere/`,
   and treat any new warning as something to fix, not ignore (the codebase currently builds with
   zero warnings — keep it that way).
6. **Every release needs all three:** an `extension.json` version bump, a `CHANGELOG.md` entry
   under a new dated heading, and (per the packaging steps in §3) a rebuilt, correctly-flat zip
   named `com.cove.heresphere-<version>.zip`. Do not bump the version without also updating the
   changelog, and vice versa.
7. **If you add a new tag category or change tag formatting,** update both the README's "Tag
   categories explained" table (user-facing) and this file's §5 table (developer-facing) in the
   same change — they must stay in sync, they're describing the same behavior at different levels
   of detail.
8. **If you're an AI agent with a scratch/working-memory file** (e.g. a `CLINE.md`-style running
   notes file) **from a previous session, re-read it before resuming work** — this project has a
   history of multi-session investigations (auth resolution, rating/favorite write-back, group
   visibility) where the reasoning and verified facts from an earlier session matter and
   re-deriving them from scratch wastes time and risks contradicting already-confirmed findings.
9. **Prefer asking a clarifying question over guessing** when a request is ambiguous about scope
   (e.g. "per-user groups" could mean several different things given Cove's data model — see §5 for
   the resolution that was actually agreed on, and don't silently reinterpret it in a future
   change without checking).
