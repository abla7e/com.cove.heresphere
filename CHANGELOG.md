# Changelog

All notable changes to the HereSphere extension (`com.cove.heresphere`) are documented
here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the extension follows semantic versioning.

## [Unreleased]

## [0.6.0] - 2026-10-05

### Added

- **Segments as timeline markers.** Cove segments are sent as timestamped tags (`start`/`end` in
  ms, `track: 0`), which HereSphere shows as chapters above the seek bar. Label = segment title,
  falling back to its tag's name. A segment without an end gets `end: 0` ("through the end"). For
  clips, the clip's own segments plus the parent's overlapping segments, shifted and trimmed to the
  clip window. Batch-loaded once per catalog/scan request. Default: only hand-made segments
  (`SourceKey == "user"`); configurable.
- **Studio tag category** (`Studio:<name>`); the video queries now include `Studio`.
- **Subtitles.** Cove's sidecar captions (`.srt`/`.vtt`, per file) are sent in the video-data
  `subtitles[]` field, labeled by language, and served by the new anonymous
  `GET /heresphere/subtitle/{captionId}.{srt|vtt}` (the extension is in the URL because HereSphere
  chooses its parser from it). Mirrors Cove's own caption endpoint, plus a check that the sidecar
  filename can't leave the video's folder.
- **Settings page** (Settings → HereSphere, above the existing debug section):
  - Reorder the tag categories with ▲/▼. **Every category is now numbered** from that order
    (`1. Tag`, `2. Group`, ... `8. VR`), which is what makes HereSphere sort them that way.
  - Segment markers: hand-made only / all (including AI) / off.
  - Subtitles on/off.
  - Settings are extension-wide, stored in Cove's extension key-value store (the extension now
    implements `IStatefulExtension`). Saving needs `extensions.configure`; everyone signed in can
    view them.
- **API token revocation.** "Your API tokens" lists the signed-in user's active tokens
  (HereSphere's non-expiring token is labeled) with a **×** to revoke one, after a confirmation.
  Uses Cove's own `RevokeApiTokenAsync`, which only revokes the caller's own tokens and writes the
  audit log.
- New endpoints: `GET`/`PUT /api/heresphere/settings`, `GET /api/heresphere/tokens`,
  `DELETE /api/heresphere/tokens/{id}`.

### Changed

- **Tag category names changed** for Resolution, Rating and VR: they are now numbered like the
  rest (`Resolution:1080p` → `6. Resolution:1080p` with the default order). Anything in HereSphere
  that matched on the old plain names (saved searches/filters) needs updating once.
- `BuildTags`, `BuildMediaEntry` and `BuildVideoData` take the request settings (and segment
  markers); the three video queries share `GetVideoAsync`/`GetAllVideosAsync`.

## [0.5.2] - 2026-10-05

### Fixed

- **Ratings/favorites sync, for real this time.** The 0.5.1 fix had no effect: it moved the
  `ICurrentPrincipalAccessor.Set(...)` call into a new `async` wrapper (`ResolveAccessAndApplyAsync`),
  but a value written to an `AsyncLocal<T>` inside *any* `async` method is discarded when that
  method's task completes, so the endpoint still ran as the anonymous principal. (The 0.5.1 local
  repro set the value directly in the endpoint body, so it never exercised the wrapper; that's why
  it passed.) Replaced it with a **synchronous** `ApplyResolvedPrincipal(http, auth)` helper that
  `CatalogAsync`, `ScanAsync`, and `VideoAsync` call right after `await ResolveAccessAsync(...)`.
  A synchronous call writes into the endpoint's own execution context, so `IUserEngagementService`
  now sees the resolved user. Verified with a repro that runs the 0.5.1 shape and the fixed shape
  side by side: 0.5.1 → service sees `Anonymous`, fixed → service sees the user.
  - Exact failure mode, confirmed from the 0.5.1 trace (`resultingCoveRating=null
    resultingIsFavorite=null`, i.e. a null snapshot, not an empty one): under the anonymous
    principal, Cove's `Video` authorization query filter hides every video. `UserEngagementService`
    checks existence through that filter (`EntityExistsAsync` / `db.Videos`), so it returned `null`
    for every read and write and saved nothing.

### Added

- The `HSP WRITE` trace line now also logs `snapshotNull`, `ambientUserId`, and `ambientKind`:
  the principal the engagement service actually acted as. A mismatch with the `HSP AUTH` line now
  shows up directly in the log.

## [0.5.1] - 2026-10-05

### Fixed

- **Ratings and favorites now actually sync both ways with HereSphere.** `ScanAsync`,
  `VideoAsync`, and `CatalogAsync` resolve the caller's Cove principal through
  `ResolveAccessAsync`, but that method's `ICurrentPrincipalAccessor.Set(principal)` calls run
  inside its own async call frame — per `AsyncLocal<T>` semantics, a value an *awaited callee*
  writes never flows back up to the awaiter once the `await` returns. So immediately after
  `var auth = await ResolveAccessAsync(...)`, the endpoint's own ambient
  `ICurrentPrincipalAccessor.Current` was still whatever Cove's `CurrentPrincipalMiddleware` had
  set before the request reached the extension — anonymous, for the steady-state `auth-token`
  header path every real HereSphere client uses — even though the *returned*
  `HereSphereAuthResolution.Principal` was correctly resolved to the real user. Every subsequent
  call into `IUserEngagementService` (`GetVideoSnapshotsAsync`, `GetSnapshotAsync`,
  `SetRatingAsync`, `SetFavoriteAsync`) reads/writes `ICurrentPrincipalAccessor.Current` directly,
  so they silently operated against "no user": reads always returned `rating=null,
  isFavorite=false`, and writes (a star rating or a heart tap in the client) persisted nothing
  and immediately echoed back empty. Per-user dynamic groups (Save for Later, Watch History,
  Continue Watching) were unaffected because they're resolved from the `principal.UserId` value
  returned by `auth`, not from the ambient accessor — which is why groups worked while
  ratings/favorites silently didn't.
  - New `ResolveAccessAndApplyAsync` helper (`HereSphereAuth.cs`) wraps `ResolveAccessAsync` and
    re-applies the resolved principal to `ICurrentPrincipalAccessor` in the *caller's* ambient
    context right after the `await` returns. `ScanAsync`, `VideoAsync`, and `CatalogAsync` now call
    this wrapper instead of `ResolveAccessAsync` directly. `AuthAsync` (no engagement calls) and the
    debug endpoint (already restores the original principal afterward; read-only) are unaffected.

## [0.5.0] - 2026-10-05

### Added

- `3. Performer:<name>` tags: one per performer credited on a video in Cove, in the catalog,
  scan, and per-video responses. `GET`/`POST /heresphere/video/{id}`, the catalog, and the scan
  endpoint now include `VideoPerformers.Performer` in their video query.
- Static Cove groups are no longer gated on `ShowInVideoLists` (that flag only controls Cove's own
  browsing UI, not HereSphere) — every static group a video belongs to now shows up both as a
  `2. Group:<name>` tag and as a `library[]` section in the catalog, regardless of that flag.
- `2. Group:<name>` tags and catalog `library[]` sections for the three built-in per-user dynamic
  groups Cove derives from engagement data: **Save for Later** (`UserBookmark`), **Watch History**,
  and **Continue Watching** (both from `UserEntityAffinity`, the latter matching Cove's own
  not-yet-complete/not-near-the-end criteria). Resolved for the authenticated user on the catalog,
  scan, and per-video endpoints.
- Trace-level request/response/write-back logging on all four data-returning endpoints
  (`HereSphereLogging.cs`), gated on `ILogger.IsEnabled(LogLevel.Trace)` so it costs nothing unless
  Cove's own runtime trace logging is active (Settings → System → "Start trace logging", or a
  persisted `Cove:LogLevel: Trace`). Logs which credential signals were present on the request
  (`auth-token` header, `Authorization` header, `?access_token=` query, `cove_access_token`
  cookie), the resolved auth source/principal, a response summary, and write-back detail.
- `CONTRIBUTING.md`: a full architecture/design reference (extension integration model, auth
  resolution order, the tag-category design, group resolution, resolution tiering, ratings
  write-back, observability, VR metadata, known gaps/future features) plus explicit instructions
  for human and AI-agent contributors.

### Changed

- **Tag values now use the Cove entity's real display name** (`Tag.Name`, not the lower-cased
  `Tag.NamespaceKey`) — tags no longer show up forced to lowercase in HereSphere regardless of how
  they're capitalized in Cove.
- **`Resolution:` tags now report a major resolution tier** (`240p`…`8640p`) instead of the video's
  exact pixel height, bucketed from the primary file's *shorter* dimension (so portrait video is
  measured by width, matching how resolution names are conventionally used in both orientations).
  A video must reach at least 90% of a tier's pixel count to qualify for it — a strict floor, not
  "nearest tier" — so a file at, say, half of 1080p's pixel count is never mislabeled `1080p`.
- `BuildTags` signature now also takes the video's primary `VideoFile` (for resolution-tier
  axis selection) alongside the existing snapshot/group-name parameters.
- `BuildVideoGroupMapAsync` and `BuildGroupSectionsAsync` now take the resolved user id so they can
  also resolve the built-in per-user dynamic groups described above; existing static-group behavior
  is preserved and extended (no longer gated on `ShowInVideoLists`).

## [0.3.0] - 2026-10-03

### Added

- Cove-driven auth across all endpoints. An explicit `auth-token` header is the
  steady-state HereSphere path — the token the client was handed and resends on
  later requests — and a token that no longer resolves is a bad login
  (`access: -1`). A standard `Authorization` header resolves the same way but
  degrades to guest (`access: 0`) when unresolvable. Username/password in the
  request body verifies against Cove, records the login (success or failure),
  revokes the previous durable token named `HereSphere`, and issues a fresh
  unscoped, non-expiring `cove_pat_*` token returned raw (no `Bearer` prefix),
  so each user keeps exactly one live HereSphere token. The resolved principal
  is set on `ICurrentPrincipalAccessor` for the request.
- Per-user rating/favorite in catalog, scan, and video-data responses: entries
  now carry the resolved user's `rating` (Cove's 0-100 converted to the
  HereSphere 0-5 scale by dividing by 20; omitted when unrated) and
  `isFavorite`, read from the user's engagement snapshots.
- Rating/favorite write-back on `POST /heresphere/video/{id}`: a resolved user
  (one with a Cove `UserId`) sending `rating` and/or `isFavorite` in the body
  has the value persisted through Cove's user-engagement service
  (`SetRatingAsync` with `aspect: "overall"`, `SetFavoriteAsync`; HereSphere
  0-5 converts back to Cove 0-100 as 20 points per star, rounded and clamped)
  and the response is rebuilt from the updated snapshot. Video-data responses
  now advertise `writeRating`/`writeFavorite` so clients know write-back is
  available.
- Static Cove group sections in the catalog `library` list: one section per
  `Group` with `Kind == Static` and `ShowInVideoLists` (the same visibility
  rule Cove applies to its own video lists) that contains at least one video
  from the loaded catalog, entries ordered by `GroupItem.OrderIndex`, each
  pointing at `/heresphere/video/{id}`.
- `HereSphereVideoRequest` model: `needsMediaSource` and `tags` are accepted
  for compatibility (Cove always provides media sources and manages tags
  itself).

### Changed

- `GET|POST /heresphere` and `GET|POST /heresphere/scan` now return empty
  `media`/`library`/`scanData` arrays when no valid user is resolved (guest or
  bad login) instead of catalog or scan data.
- Replaced the `HereSphereCredentials` request model with the combined
  `HereSphereVideoRequest` used by the catalog, auth, scan, and video-data
  endpoints.
- `extension.json` description updated to reflect Cove-driven auth and
  per-user rating/favorite write-back.

## [0.2.2] - 2026-10-03

### Fixed

- Thumbnails now resolve for anonymous (credential-less) requests — e.g. a catalog
  `<img>` or a direct browser navigation that carries no HereSphere `auth-token`.
  `GET /heresphere/thumbnail/{id}` now runs the generated-cover lookup
  (`IStreamService.GetVideoScreenshot`) under a temporary system principal and restores
  the caller's principal in `finally`. Cove's `CoveContext` authorization query filters
  return no `Video` rows for Cove's anonymous principal (which has an empty permission
  set), so the lookup returned null and the endpoint 404'd even though the cover existed
  on disk. This mirrors the principal-scope pattern Cove itself uses in
  `DuplicateResolutionService`. Custom covers were already anonymous-safe (blob lookup
  bypasses the filtered queries).
- Negative thumbnail lookups (Cove has no cover yet) now send `Cache-Control: no-store`,
  so clients don't pin a 404 across the asynchronous cover generation.

### Changed

- `GET /heresphere/thumbnail/{id}` doc comment updated to describe anonymous resolution.

## [0.2.1] - 2026-10-02

### Fixed

- Thumbnails no longer 404 for every video. `Video.ImageBlobId` is stored as an empty
  string (not NULL) when a video has no custom cover, and 0.2.0 checked
  `ImageBlobId is not null`. Both `CoveGeneratedAssets.HasThumbnail` and
  `ThumbnailAsync` now guard with `string.IsNullOrWhiteSpace` — matching Cove — and
  `ThumbnailAsync` falls through to the generated-cover path when a set blob is missing
  on disk instead of returning 404 early (Cove's behavior).

## [0.2.0] - 2026-10-02

### Added

- `thumbnailImage` now mirrors Cove's `GET /api/videos/{id}/image` resolution: custom
  cover blob, then timestamped frame (clips), then sprite frame, then the generated
  cover of the source video. The catalog and video-data builders advertise the URL only
  when the asset actually exists, so HereSphere clients never receive a dead thumbnail
  URL.
- New `GET /heresphere/preview/{id}` endpoint and `thumbnailVideo` field: the source
  video's hover-preview mp4, served with byte-range support and Cove's cache headers
  (404 when no preview has been generated).
- New `CoveGeneratedAssets` helper replicating Cove's generated-asset path layout
  (`screenshots/{sub}/{id}.jpg`, `screenshots/{sub}/{id}_t{sec}.jpg`,
  `previews/{sub}/{id}.mp4`, `vtt/{sub}/{id}_sprite.jpg` + `vtt/{sub}/{id}_thumbs.vtt`)
  to answer "does this video have a thumbnail/preview in Cove?" without running the
  generation job.

### Changed

- `GET /heresphere/thumbnail/{id}` performs full Cove resolution with Cove's cache
  headers (`public, max-age=86400` for generated assets; `no-store, no-cache, ...`
  otherwise).

## [0.1.0] - 2026-10-02

### Added

- Initial release. Exposes the Cove library to HereSphere v1 clients:
  - `GET|POST /heresphere` — catalog (`access`, `auth-token`, `library`, inlined
    `media` entries).
  - `POST /heresphere/auth` — exchange Cove credentials for a HereSphere `auth-token`.
  - `GET|POST /heresphere/video/{id}` — video data; with `?file={fileId}` streams that
    file.
  - `GET /heresphere/media/{id}` — stream the primary file (or `?file={fileId}`).
  - `GET|POST /heresphere/scan` — bulk scan metadata.
  - `GET /heresphere/thumbnail/{id}` — stream the video's custom-cover thumbnail blob.
- VR fields (`projection`, `stereo`, `fov`, `lens`, `cameraIPD`, `isEyeSwapped`)
  derived per video with `VrDescriptorDetector.Resolve`.
- Media source URLs point at the streaming endpoints so external (non-cookie) clients
  can play; files flagged unreadable (`IsSourceKnownUnreadable`) are excluded from
  `sources`.
