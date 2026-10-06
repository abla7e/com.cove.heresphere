using System.Security.Cryptography;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace HereSphereExtension;

/// <summary>
/// Mirrors Cove's on-disk generated-asset layout so the extension only advertises a thumbnail or
/// preview when Cove itself would have one. Cove stores these under <c>GeneratedPath</c> with a
/// two-hex-char SHA256 subdirectory per video id (see Cove's <c>ThumbnailService</c>):
/// <c>screenshots/{sub}/{id}.jpg</c> (generated cover), <c>screenshots/{sub}/{id}_t{sec}.jpg</c>
/// (timestamped clip frame), <c>previews/{sub}/{id}.mp4</c> (hover preview), <c>vtt/{sub}/{id}_sprite.jpg</c>
/// + <c>vtt/{sub}/{id}_thumbs.vtt</c> (sprite frame).
/// </summary>
internal static class CoveGeneratedAssets
{
    private static string SubDir(int videoId)
        => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(videoId)))[..2];

    public static string CoverPath(CoveConfiguration config, int videoId)
        => Path.Combine(config.GeneratedPath, "screenshots", SubDir(videoId), $"{videoId}.jpg");

    public static string TimestampedPath(CoveConfiguration config, int videoId, double seconds)
        => Path.Combine(config.GeneratedPath, "screenshots", SubDir(videoId), $"{videoId}_t{(int)seconds}.jpg");

    public static string PreviewPath(CoveConfiguration config, int videoId)
        => Path.Combine(config.GeneratedPath, "previews", SubDir(videoId), $"{videoId}.mp4");

    public static string SpritePath(CoveConfiguration config, int videoId)
        => Path.Combine(config.GeneratedPath, "vtt", SubDir(videoId), $"{videoId}_sprite.jpg");

    public static string SpriteVttPath(CoveConfiguration config, int videoId)
        => Path.Combine(config.GeneratedPath, "vtt", SubDir(videoId), $"{videoId}_thumbs.vtt");

    /// <summary>
    /// Whether Cove's <c>GET /api/videos/{id}/image</c> would return an image for this video:
    /// a custom cover blob, a timestamped/sprite frame for clips with a start offset, or the
    /// generated cover of the source video (the parent, for clips).
    /// </summary>
    public static bool HasThumbnail(Video video, CoveConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(video.ImageBlobId))
            return true;

        var sourceVideoId = video.ParentVideoId ?? video.Id;

        if (video.ClipStartSec is not null)
        {
            if (File.Exists(TimestampedPath(config, sourceVideoId, video.ClipStartSec.Value)))
                return true;
            if (File.Exists(SpritePath(config, sourceVideoId)) && File.Exists(SpriteVttPath(config, sourceVideoId)))
                return true;
        }

        return File.Exists(CoverPath(config, sourceVideoId));
    }

    /// <summary>
    /// The generated hover-preview mp4 for a source video if it exists on disk, else null —
    /// matching Cove's <c>GET /api/stream/video/{id}/preview</c>.
    /// </summary>
    public static string? GetExistingPreviewPath(CoveConfiguration config, int sourceVideoId)
    {
        var path = PreviewPath(config, sourceVideoId);
        return File.Exists(path) ? path : null;
    }
}