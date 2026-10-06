using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HereSphereExtension;

public sealed partial class HereSphereExtension
{
    /// <summary>Bulk scan metadata: one entry per video (search/sort index data).</summary>
    private async Task<IResult> ScanAsync(HttpContext http, CancellationToken ct)
    {
        var logger = http.RequestServices.GetRequiredService<ILogger<HereSphereExtension>>();
        var request = await ReadVideoRequestAsync(http, ct);
        TraceRequest(http, logger, request);
        var auth = await ResolveAccessAsync(http, request, http.RequestServices, ct);
        ApplyResolvedPrincipal(http, auth);
        TraceAuthResolution(http, logger, auth);
        var principal = auth.Principal;

        // No resolved user (guest or bad login) → empty scan data; the HereSphere client handles
        // the empty state gracefully.
        if (principal is null || principal.UserId is null)
        {
            var empty = new HereSphereScanResponse();
            TraceResponse(http, logger, empty);
            http.Response.Headers["HereSphere-JSON-Version"] = "1";
            return Results.Json(empty);
        }

        var db = http.RequestServices.GetRequiredService<DbContext>();
        var settings = await GetSettingsAsync(ct);
        var videos = await GetAllVideosAsync(db, ct);

        var snapshots = await http.RequestServices.GetRequiredService<IUserEngagementService>()
            .GetVideoSnapshotsAsync(videos.Select(v => v.Id), ct);
        var groupMap = await BuildVideoGroupMapAsync(db, videos.Select(v => v.Id), principal.UserId, ct);
        var segmentMap = await BuildSegmentMarkerMapAsync(db, videos, settings, ct);

        var response = new HereSphereScanResponse
        {
            ScanData = videos.Select(v =>
            {
                snapshots.TryGetValue(v.Id, out var snapshot);
                return new HereSphereScanEntry
                {
                    Link = BuildUrl(http, $"/heresphere/video/{v.Id}"),
                    Title = ResolveTitle(v),
                    DateAdded = v.CreatedAt.ToString("yyyy-MM-dd"),
                    DateReleased = v.Date?.ToString("yyyy-MM-dd"),
                    Duration = ToMilliseconds(v.MaxDuration),
                    Rating = ToHspRating(snapshot?.Rating),
                    IsFavorite = snapshot?.IsFavorite ?? false,
                    Tags = BuildTags(v, PickPrimaryFile(v), snapshot, groupMap.GetValueOrDefault(v.Id),
                        settings, segmentMap.GetValueOrDefault(v.Id)),
                };
            }).ToList(),
        };

        TraceResponse(http, logger, response);
        http.Response.Headers["HereSphere-JSON-Version"] = "1";
        return Results.Json(response);
    }

    /// <summary>
    /// Video data (JSON) for one video. When <c>?file={fileId}</c> is present, the selected file is
    /// streamed instead, so a source URL can point straight at this route.
    /// </summary>
    private async Task<IResult> VideoAsync(HttpContext http, CancellationToken ct)
    {
        var logger = http.RequestServices.GetRequiredService<ILogger<HereSphereExtension>>();
        if (!int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
            return Results.BadRequest();

        if (int.TryParse(http.Request.Query["file"].ToString(), out var fileId))
            return await StreamFileAsync(http, id, fileId, ct);

        var db = http.RequestServices.GetRequiredService<DbContext>();
        var video = await GetVideoAsync(db, id, ct);
        if (video is null)
            return Results.NotFound(new { access = -1 });

        var request = await ReadVideoRequestAsync(http, ct);
        TraceRequest(http, logger, request);
        var auth = await ResolveAccessAsync(http, request, http.RequestServices, ct);
        ApplyResolvedPrincipal(http, auth);
        TraceAuthResolution(http, logger, auth);
        var access = auth.Access;
        var token = auth.Token;
        var principal = auth.Principal;
        var canWrite = principal is not null && principal.UserId is not null;

        // Write back any rating/favorite carried in the request for the resolved user, then
        // rebuild the response from the updated engagement snapshot.
        UserEngagementSnapshot? snapshot = null;
        if (canWrite)
        {
            var engagement = http.RequestServices.GetRequiredService<IUserEngagementService>();
            if (request?.Rating is { } rating)
                snapshot = await engagement.SetRatingAsync(
                    AffinityHostType.Video, id, ToCoveRating(rating), aspect: "overall", ct);
            if (request?.IsFavorite is { } favorite)
                snapshot = await engagement.SetFavoriteAsync(
                    AffinityHostType.Video, id, favorite, ct);
            snapshot ??= await engagement.GetSnapshotAsync(AffinityHostType.Video, id, ct);
            TraceWriteBack(http, logger, id, request, snapshot);
        }

        var config = http.RequestServices.GetRequiredService<CoveConfiguration>();
        var settings = await GetSettingsAsync(ct);
        var groupNames = (await BuildVideoGroupMapAsync(db, [id], principal?.UserId, ct)).GetValueOrDefault(id);
        var segments = (await BuildSegmentMarkerMapAsync(db, [video], settings, ct)).GetValueOrDefault(id);

        var videoData = BuildVideoData(http, video, access, token, config, settings, snapshot, canWrite, groupNames, segments);
        TraceResponse(http, logger, id, videoData);
        http.Response.Headers["HereSphere-JSON-Version"] = "1";
        return Results.Json(videoData);
    }

    /// <summary>Stream a video file (primary by default, or a specific one via <c>?file=</c>).</summary>
    private async Task<IResult> MediaAsync(HttpContext http, CancellationToken ct)
    {
        if (!int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
            return Results.BadRequest();

        var fileId = int.TryParse(http.Request.Query["file"].ToString(), out var f) ? f : (int?)null;
        return await StreamFileAsync(http, id, fileId, ct);
    }

    /// <summary>
    /// Serves the image Cove's UI would show for this video, so a catalog <c>thumbnailImage</c>
    /// always matches the Cove UI. Resolution mirrors Cove's <c>GET /api/videos/{id}/image</c>:
    /// the video's custom cover blob when set, otherwise the cover screenshot (timestamped/sprite
    /// frame for clips with a start offset) or the generated cover of the source video. 404 when
    /// Cove has no image for the video. Anonymous clients resolve the same assets (see the
    /// system-principal lookup below), so an <c>&lt;img&gt;</c> request without Cove credentials
    /// is not left with a dead link.
    /// </summary>
    private async Task<IResult> ThumbnailAsync(HttpContext http, CancellationToken ct)
    {
        if (!int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
            return Results.BadRequest();

        var services = http.RequestServices;
        var db = services.GetRequiredService<DbContext>();

        var video = await db.Set<Video>().IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new { v.Id, v.ImageBlobId })
            .FirstOrDefaultAsync(ct);
        if (video is null)
            return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(video.ImageBlobId))
        {
            var blob = await services.GetRequiredService<IBlobService>().GetBlobAsync(video.ImageBlobId, ct);
            if (blob is not null)
            {
                var (stream, contentType) = blob.Value;
                if (stream.CanSeek)
                    stream.Seek(0, System.IO.SeekOrigin.Begin);

                http.Response.Headers["Cache-Control"] = "public, max-age=86400";
                return Results.File(stream, contentType);
            }

            // Blob set but missing on disk — fall through to the generated cover, as Cove does.
        }

        // Cove's StreamService re-resolves the video through CoveContext's authorization query
        // filters, which return no rows for an unauthenticated request (Cove's anonymous
        // principal has no permissions). This endpoint is AllowCoveAnonymous and must resolve
        // assets for anonymous HereSphere clients — an <c>&lt;img&gt;</c> request cannot carry
        // the client's API token — so run the lookup as the system principal, which bypasses
        // Cove's authorization filters, and restore the caller's principal afterwards.
        var principalAccessor = services.GetRequiredService<ICurrentPrincipalAccessor>();
        var callerPrincipal = principalAccessor.Current;
        principalAccessor.Set(CovePrincipal.System());
        try
        {
            var screenshot = await services.GetRequiredService<IStreamService>().GetVideoScreenshot(id, null, ct);
            if (screenshot is null)
            {
                // The cover may exist on a later request (generation is asynchronous); do not
                // let the client cache this negative answer.
                http.Response.Headers["Cache-Control"] = "no-store";
                return Results.NotFound();
            }

            var (screenshotStream, screenshotContentType, useLongCache) = screenshot.Value;
            http.Response.Headers["Cache-Control"] = useLongCache
                ? "public, max-age=86400"
                : "no-store, no-cache, max-age=0, must-revalidate";
            return Results.File(screenshotStream, screenshotContentType);
        }
        finally
        {
            principalAccessor.Set(callerPrincipal);
        }
    }

    /// <summary>
    /// Serves one Cove sidecar caption (<c>.srt</c>/<c>.vtt</c>) as a HereSphere external subtitle.
    /// The route ends in the caption's extension (<c>/heresphere/subtitle/{captionId}.srt</c>)
    /// because HereSphere chooses its subtitle parser from the URL's file extension. Mirrors Cove's
    /// <c>GET /api/stream/video/{id}/caption/{captionId}</c>: the caption row's filename is resolved
    /// next to its video file. Anonymous, like the other media routes (HereSphere loads subtitles
    /// without its auth token); only files Cove's scanner recorded as captions can be served, and a
    /// filename that tries to leave the video's folder is rejected.
    /// </summary>
    private async Task<IResult> SubtitleAsync(HttpContext http, CancellationToken ct)
    {
        var raw = http.Request.RouteValues["file"]?.ToString() ?? string.Empty;
        var dot = raw.LastIndexOf('.');
        if (dot <= 0 || !int.TryParse(raw[..dot], out var captionId))
            return Results.NotFound();
        var requestedExt = raw[(dot + 1)..].ToLowerInvariant();

        var db = http.RequestServices.GetRequiredService<DbContext>();
        var caption = await db.Set<VideoCaption>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.File)
            .FirstOrDefaultAsync(c => c.Id == captionId, ct);
        if (caption?.File is null)
            return Results.NotFound();

        var ext = NormalizeCaptionType(caption.CaptionType);
        if (ext is null || ext != requestedExt)
            return Results.NotFound();

        var videoDir = Path.GetDirectoryName(caption.File.Path);
        if (string.IsNullOrEmpty(videoDir)
            || string.IsNullOrWhiteSpace(caption.Filename)
            || caption.Filename != Path.GetFileName(caption.Filename))
            return Results.NotFound();

        var path = Path.Combine(videoDir, caption.Filename);
        if (!File.Exists(path))
            return Results.NotFound();

        http.Response.Headers["Cache-Control"] = "public, max-age=3600";
        var contentType = ext == "srt" ? "application/x-subrip" : "text/vtt";
        return Results.File(path, contentType, fileDownloadName: null, enableRangeProcessing: false);
    }

    /// <summary>
    /// Serves the generated hover-preview clip Cove's UI plays on video cards (the
    /// <c>GeneratedPath/previews</c> mp4 of the source video, parent for clips), with byte-range
    /// support. 404 when no preview has been generated, mirroring Cove's
    /// <c>GET /api/stream/video/{id}/preview</c>.
    /// </summary>
    private async Task<IResult> PreviewAsync(HttpContext http, CancellationToken ct)
    {
        if (!int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
            return Results.BadRequest();

        var services = http.RequestServices;
        var config = services.GetRequiredService<CoveConfiguration>();
        var db = services.GetRequiredService<DbContext>();

        var video = await db.Set<Video>().IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new { v.Id, v.ParentVideoId })
            .FirstOrDefaultAsync(ct);
        if (video is null)
            return Results.NotFound();

        var path = CoveGeneratedAssets.GetExistingPreviewPath(config, video.ParentVideoId ?? video.Id);
        if (path is null)
            return Results.NotFound();

        http.Response.Headers["Cache-Control"] = "public, max-age=86400";
        http.Response.Headers["Accept-Ranges"] = "bytes";
        return Results.File(path, "video/mp4", enableRangeProcessing: true);
    }
}