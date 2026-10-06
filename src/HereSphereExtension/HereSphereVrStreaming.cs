using Cove.Core.Common;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HereSphereExtension;

public sealed partial class HereSphereExtension
{
    private sealed record HereSphereVr(
        string Projection, string? Stereo, double? Fov, string? Lens, double CameraIPD, bool IsEyeSwapped);

    /// <summary>Maps a video's VR descriptor (explicit values, then file-name, then dimensions) to HereSphere fields.</summary>
    private static HereSphereVr? BuildVr(Video video, VideoFile? file)
    {
        if (!video.IsVr)
            return null;

        var descriptor = VrDescriptorDetector.Resolve(
            video.IsVr,
            video.VrProjection,
            video.VrFieldOfView,
            video.VrStereoMode,
            file?.Path,
            file?.Width ?? 0,
            file?.Height ?? 0);
        if (descriptor is null)
            return null;

        var projection = (descriptor.Projection, descriptor.FieldOfView) switch
        {
            (VrProjection.Equirectangular, 360) => "equirectangular360",
            (VrProjection.Equirectangular, _) => "equirectangular",
            (VrProjection.Fisheye, _) or (VrProjection.Mkx200, _) => "fisheye",
            (VrProjection.Flat, _) => "perspective",
            _ => "equirectangular",
        };

        var stereo = descriptor.StereoMode switch
        {
            VrStereoMode.SideBySide => "sbs",
            VrStereoMode.TopBottom => "tb",
            _ => null,
        };

        return new HereSphereVr(
            Projection: projection,
            Stereo: stereo,
            Fov: descriptor.FieldOfView > 0 ? (double)descriptor.FieldOfView : null,
            Lens: descriptor.Projection == VrProjection.Mkx200 ? "MKX200" : "Linear",
            CameraIPD: 6.5,
            IsEyeSwapped: false);
    }

    /// <summary>
    /// Resolves a display title the same way the Cove UI does: an explicit title, otherwise the
    /// primary file's basename (full filename, extension included), otherwise the video code,
    /// otherwise a generic placeholder.
    /// </summary>
    private static string ResolveTitle(Video video)
    {
        if (!string.IsNullOrWhiteSpace(video.Title))
            return video.Title;
        var file = PickPrimaryFile(video);
        if (!string.IsNullOrWhiteSpace(file?.Basename))
            return file!.Basename;
        if (!string.IsNullOrWhiteSpace(video.Code))
            return video.Code;
        return "Untitled";
    }

    private static long? ToMilliseconds(double seconds)
        => seconds > 0 ? (long)Math.Round(seconds * 1000) : null;

    private static string BuildUrl(HttpContext http, string path)
    {
        var host = http.Request.Host.HasValue
            ? http.Request.Host.Value
            : http.Request.Headers.Host.ToString();
        if (string.IsNullOrEmpty(host))
            return path;
        return $"{http.Request.Scheme}://{host}{path}";
    }

    /// <summary>One video with everything the single-video response needs, including sidecar captions.</summary>
    private static async Task<Video?> GetVideoAsync(DbContext db, int id, CancellationToken ct)
        => await db.Set<Video>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(v => v.Files).ThenInclude(f => f.Captions)
            .Include(v => v.Studio)
            .Include(v => v.VideoTags).ThenInclude(t => t.Tag)
            .Include(v => v.VideoPerformers).ThenInclude(p => p.Performer)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    /// <summary>
    /// Every video, newest first, with what the catalog and scan responses need (no captions: those
    /// are only part of the single-video response).
    /// </summary>
    private static async Task<List<Video>> GetAllVideosAsync(DbContext db, CancellationToken ct)
        => await db.Set<Video>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(v => v.Files)
            .Include(v => v.Studio)
            .Include(v => v.VideoTags).ThenInclude(t => t.Tag)
            .Include(v => v.VideoPerformers).ThenInclude(p => p.Performer)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Streams a video file (the primary file by default, or a specific one via <c>?file=</c>) as a
    /// seekable, range-capable stream. Resolves the source video and file the same way Cove's
    /// StreamService does: a clip/child video's media lives on its parent, so the file is looked up
    /// by the resolved source video id, not the requested id.
    /// </summary>
    private async Task<IResult> StreamFileAsync(HttpContext http, int videoId, int? fileId, CancellationToken ct)
    {
        var db = http.RequestServices.GetRequiredService<DbContext>();

        // Mirror Cove's ResolveSourceVideoIdAsync: a child video's media lives on its parent.
        var video = await db.Set<Video>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.Id == videoId)
            .Select(v => new { v.Id, v.ParentVideoId, v.PrimaryFileId })
            .FirstOrDefaultAsync(ct);
        if (video is null)
            return Results.NotFound();

        var sourceVideoId = video.ParentVideoId ?? video.Id;
        var selectedFileId = fileId ?? video.PrimaryFileId;
        if (selectedFileId is null)
            return Results.NotFound();

        // The requested (or primary) file, scoped to the source video — Cove looks up VideoFiles by
        // (Id, VideoId == sourceVideoId).
        var file = await db.Set<VideoFile>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == selectedFileId.Value && f.VideoId == sourceVideoId, ct);
        if (file is null)
            return Results.NotFound();

        var path = FilesystemPaths.ToNativePath(file.Path);
        if (!System.IO.File.Exists(path))
            return Results.NotFound();

        // Let the framework open, own, and dispose the file (no manually-opened stream) and enable
        // range processing so ASP.NET answers HTTP Range requests with 206/Content-Range — the
        // minimal-API equivalent of Cove's StreamController (File(stream, contentType,
        // enableRangeProcessing: true) + Accept-Ranges: bytes). The old code opened the FileStream
        // itself with `using var`, disposing the handle before the result executed
        // (ObjectDisposedException -> HTTP 500 on every request).
        http.Response.Headers["Accept-Ranges"] = "bytes";
        return Results.File(path, GetContentType(file), enableRangeProcessing: true);
    }

    /// <summary>video/* content types by container format or file extension (Cove-matching).</summary>
    private static readonly Dictionary<string, string> VideoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp4"] = "video/mp4",
        ["m4v"] = "video/mp4",
        ["mov"] = "video/quicktime",
        ["webm"] = "video/webm",
        ["mkv"] = "video/x-matroska",
        ["avi"] = "video/x-msvideo",
        ["ts"] = "video/mp2t",
        ["mts"] = "video/mp2t",
        ["m2ts"] = "video/mp2t",
        ["mpg"] = "video/mpeg",
        ["mpeg"] = "video/mpeg",
    };

    private static string GetContentType(VideoFile file)
    {
        // Prefer the stored container format; fall back to the on-disk extension when it is empty.
        var format = string.IsNullOrWhiteSpace(file.Format)
            ? System.IO.Path.GetExtension(file.Path)?.TrimStart('.') ?? string.Empty
            : file.Format;
        return VideoContentTypes.TryGetValue(format, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}