using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace HereSphereExtension;

public sealed partial class HereSphereExtension
{
    /// <summary>
    /// The thumbnail URL, only when Cove would actually have an image for this video (custom cover
    /// blob, clip timestamped/sprite frame, or generated cover) — never a dead link.
    /// </summary>
    private static string? BuildThumbnailImage(HttpContext http, Video video, CoveConfiguration config)
        => CoveGeneratedAssets.HasThumbnail(video, config)
            ? BuildUrl(http, $"/heresphere/thumbnail/{video.Id}")
            : null;

    /// <summary>
    /// The hover-preview URL, only when Cove has a generated preview clip for the source video.
    /// </summary>
    private static string? BuildThumbnailVideo(HttpContext http, Video video, CoveConfiguration config)
        => CoveGeneratedAssets.GetExistingPreviewPath(config, video.ParentVideoId ?? video.Id) is not null
            ? BuildUrl(http, $"/heresphere/preview/{video.Id}")
            : null;

    /// <summary>
    /// Converts a Cove 0-100 rating to the HereSphere 0-5 scale (one Cove point is one-hundredth
    /// of a star). Returns null when the user has not rated the video.
    /// </summary>
    private static double? ToHspRating(int? coveRating)
        => coveRating is null ? null : coveRating.Value / 20.0;

    /// <summary>Converts a HereSphere 0-5 rating to Cove's 0-100 scale (20 Cove points per star), clamped to [0, 100].</summary>
    private static int ToCoveRating(double hspRating)
    {
        var rounded = (int)Math.Round(Math.Clamp(hspRating, 0d, 5d) * 20d, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, 0, 100);
    }

    /// <summary>Builds a HereSphere catalog media entry (metadata plus playable sources).</summary>
    private static HereSphereMediaEntry BuildMediaEntry(
        HttpContext http, Video video, CoveConfiguration config, HereSphereRequestSettings settings,
        UserEngagementSnapshot? snapshot = null, IReadOnlyList<string>? groupNames = null,
        IReadOnlyList<SegmentMarker>? segments = null)
    {
        var primary = PickPrimaryFile(video);
        var vr = BuildVr(video, primary);
        return new HereSphereMediaEntry
        {
            Id = video.Id,
            Name = ResolveTitle(video),
            Description = string.IsNullOrWhiteSpace(video.Details) ? null : video.Details,
            ThumbnailImage = BuildThumbnailImage(http, video, config),
            ThumbnailVideo = BuildThumbnailVideo(http, video, config),
            DateAdded = video.CreatedAt.ToString("yyyy-MM-dd"),
            DateReleased = video.Date?.ToString("yyyy-MM-dd"),
            Duration = ToMilliseconds(video.MaxDuration),
            Rating = ToHspRating(snapshot?.Rating),
            IsFavorite = snapshot?.IsFavorite ?? false,
            Projection = vr?.Projection,
            Stereo = vr?.Stereo,
            Fov = vr?.Fov,
            Lens = vr?.Lens,
            CameraIPD = vr?.CameraIPD,
            IsEyeSwapped = vr?.IsEyeSwapped,
            Tags = BuildTags(video, primary, snapshot, groupNames, settings, segments),
            Sources = BuildSources(http, video),
        };
    }

    /// <summary>Builds the strict HereSphere v1 video-data payload for one video.</summary>
    private static HereSphereVideoDataResponse BuildVideoData(
        HttpContext http, Video video, int access, string? token, CoveConfiguration config,
        HereSphereRequestSettings settings,
        UserEngagementSnapshot? snapshot = null, bool canWrite = false,
        IReadOnlyList<string>? groupNames = null, IReadOnlyList<SegmentMarker>? segments = null)
    {
        var primary = PickPrimaryFile(video);
        var vr = BuildVr(video, primary);
        var sources = BuildSources(http, video);
        return new HereSphereVideoDataResponse
        {
            Access = access,
            AuthToken = token,
            Title = ResolveTitle(video),
            Description = string.IsNullOrWhiteSpace(video.Details) ? null : video.Details,
            ThumbnailImage = BuildThumbnailImage(http, video, config),
            ThumbnailVideo = BuildThumbnailVideo(http, video, config),
            DateAdded = video.CreatedAt.ToString("yyyy-MM-dd"),
            DateReleased = video.Date?.ToString("yyyy-MM-dd"),
            Duration = ToMilliseconds(video.MaxDuration),
            Rating = ToHspRating(snapshot?.Rating),
            IsFavorite = snapshot?.IsFavorite ?? false,
            WriteFavorite = canWrite,
            WriteRating = canWrite,
            Projection = vr?.Projection,
            Stereo = vr?.Stereo,
            Fov = vr?.Fov,
            Lens = vr?.Lens,
            CameraIPD = vr?.CameraIPD,
            IsEyeSwapped = vr?.IsEyeSwapped,
            Tags = BuildTags(video, primary, snapshot, groupNames, settings, segments),
            Subtitles = settings.Settings.SubtitlesEnabled ? BuildSubtitles(http, video) : null,
            Media =
            [
                new HereSphereMediaTrack { Name = "Original", Sources = sources },
            ],
        };
    }

    /// <summary>
    /// Cove's sidecar captions (recorded per file by Cove's scanner) as HereSphere external
    /// subtitles, served by <c>GET /heresphere/subtitle/{captionId}.{srt|vtt}</c>. The URL ends in
    /// the caption's real extension because HereSphere picks its subtitle parser from it. Returns
    /// null (field omitted) when the video has no captions. Requires the video query to include
    /// <c>Files.Captions</c>.
    /// </summary>
    private static List<HereSphereSubtitle>? BuildSubtitles(HttpContext http, Video video)
    {
        var subtitles = new List<HereSphereSubtitle>();
        foreach (var caption in video.Files.SelectMany(f => f.Captions).OrderBy(c => c.LanguageCode).ThenBy(c => c.Id))
        {
            var ext = NormalizeCaptionType(caption.CaptionType);
            if (ext is null)
                continue;
            var language = DescribeLanguage(caption.LanguageCode);
            subtitles.Add(new HereSphereSubtitle
            {
                Name = $"{language} ({ext})",
                Language = language,
                Url = BuildUrl(http, $"/heresphere/subtitle/{caption.Id}.{ext}"),
            });
        }
        return subtitles.Count > 0 ? subtitles : null;
    }

    /// <summary>"srt" or "vtt" (the only caption types Cove records), else null.</summary>
    private static string? NormalizeCaptionType(string? type)
        => type?.Trim().ToLowerInvariant() switch
        {
            "srt" => "srt",
            "vtt" => "vtt",
            _ => null,
        };

    /// <summary>
    /// English name for an ISO 639-1 code ("en" → "English"); "00"/blank → "Unknown". Tries the
    /// runtime's culture data first and falls back to the raw code (e.g. in invariant-globalization
    /// containers, where culture names aren't available).
    /// </summary>
    private static string DescribeLanguage(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "00")
            return "Unknown";
        try
        {
            var culture = CultureInfo.GetCultureInfo(trimmed);
            var name = culture.EnglishName;
            // Invariant mode / unknown codes echo the code back (sometimes as "Unknown language (xx)").
            if (!string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                return name;
        }
        catch (CultureNotFoundException)
        {
        }
        return trimmed.ToUpperInvariant();
    }

    /// <summary>Every playable file as a source, each pointing at this extension's streaming route.</summary>
    private static List<HereSphereSource> BuildSources(HttpContext http, Video video)
    {
        var sources = new List<HereSphereSource>();
        foreach (var file in video.Files)
        {
            if (file.IsSourceKnownUnreadable)
                continue;

            int? height = file.Height > 0 ? file.Height : null;
            int? width = file.Width > 0 ? file.Width : null;
            sources.Add(new HereSphereSource
            {
                Resolution = height,
                Height = height,
                Width = width,
                Size = file.Size,
                Url = BuildUrl(http, $"/heresphere/video/{video.Id}?file={file.Id}"),
            });
        }
        return sources;
    }

    private static VideoFile? PickPrimaryFile(Video video)
    {
        if (video.PrimaryFileId is { } pid)
        {
            var primary = video.Files.FirstOrDefault(f => f.Id == pid);
            if (primary is not null)
                return primary;
        }

        return video.Files
            .Where(f => !f.IsSourceKnownUnreadable)
            .OrderByDescending(f => (long)f.Height * Math.Max(f.Width, 1))
            .ThenByDescending(f => f.Duration)
            .FirstOrDefault();
    }

    /// <summary>
    /// The standard "major" resolution tiers tags are bucketed into, ascending. Covers common
    /// broadcast/web tiers (240–1080), desktop/VR tiers (1440/1600), UHD (2160), oversized VR
    /// stereo-frame heights (2880/3240/3840) and high-end VR capture heights (4320/5760/7680/8640).
    /// </summary>
    private static readonly int[] ResolutionTiers =
        [240, 360, 480, 540, 720, 900, 1080, 1440, 1600, 2160, 2880, 3240, 3840, 4320, 5760, 7680, 8640];

    /// <summary>
    /// A video qualifies for a tier only once it reaches at least this fraction of the tier's pixel
    /// count — deliberately strict (not "nearest tier") so e.g. a 540px-tall file is never labeled
    /// "1080p": at 50% of 1080 it is perceptually a materially lower resolution, not "close enough".
    /// </summary>
    private const double ResolutionTierThreshold = 0.9;

    /// <summary>
    /// Buckets a raw pixel dimension down to the nearest major resolution tier it actually reaches
    /// (<see cref="ResolutionTierThreshold"/> of that tier's value), walking the ladder from the top.
    /// Dimensions above the top tier are reported at the top tier; dimensions below the bottom tier
    /// still fall through to the bottom tier (there is no "below everything" category).
    /// </summary>
    private static int ResolveMajorResolutionTier(int axis)
    {
        for (var i = ResolutionTiers.Length - 1; i >= 0; i--)
        {
            if (axis >= ResolutionTiers[i] * ResolutionTierThreshold)
                return ResolutionTiers[i];
        }
        return ResolutionTiers[0];
    }

    /// <summary>
    /// The video's "quality axis" for resolution tagging: the shorter of the primary file's two
    /// dimensions. This is the dimension conventional resolution names (e.g. "1080p") actually
    /// refer to in both orientations — a 1920x1080 landscape file and a 1080x1920 portrait file are
    /// both "1080p" — so portrait content naturally resolves against width instead of height.
    /// Falls back to the denormalized <see cref="Video.MaxHeight"/> when no file has dimension data.
    /// </summary>
    private static int ResolveResolutionAxis(Video video, VideoFile? primary)
    {
        if (primary is { Width: > 0, Height: > 0 })
            return Math.Min(primary.Width, primary.Height);
        return video.MaxHeight;
    }

    /// <summary>
    /// Builds the video's HereSphere tag list. HereSphere derives each tag's category from the text
    /// before the first ":" in the tag name (no colon puts it under "Uncategorized"), so every tag is
    /// a full "Category:Value" string. Every category is numbered ("1. Tag", "2. Group", ...) in the
    /// order configured on the settings page, which is what makes HereSphere sort the categories in
    /// that order. Tags are emitted category by category in the same order.
    /// </summary>
    private static List<HereSphereTag> BuildTags(
        Video video, VideoFile? primary, UserEngagementSnapshot? snapshot, IReadOnlyList<string>? groupNames,
        HereSphereRequestSettings settings, IReadOnlyList<SegmentMarker>? segments = null)
    {
        var tags = new List<HereSphereTag>();
        foreach (var category in settings.Settings.TagCategoryOrder)
            AddCategoryTags(tags, category, settings.Prefix(category), video, primary, snapshot, groupNames, segments);
        return tags;
    }

    private static void AddCategoryTags(
        List<HereSphereTag> tags, string category, string prefix, Video video, VideoFile? primary,
        UserEngagementSnapshot? snapshot, IReadOnlyList<string>? groupNames, IReadOnlyList<SegmentMarker>? segments)
    {
        switch (category)
        {
            case HereSphereTagCategories.Tag:
                // Cove tag display names (properly cased), not the lowercased namespace key.
                foreach (var name in video.VideoTags.Select(t => t.Tag?.Name))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        tags.Add(new HereSphereTag($"{prefix}:{name}"));
                }
                break;

            case HereSphereTagCategories.Group:
                // Static groups plus the per-user dynamic groups (Save for Later, Watch History,
                // Continue Watching) the video currently qualifies for.
                foreach (var name in groupNames ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        tags.Add(new HereSphereTag($"{prefix}:{name}"));
                }
                break;

            case HereSphereTagCategories.Performer:
                foreach (var name in video.VideoPerformers.Select(p => p.Performer?.Name))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        tags.Add(new HereSphereTag($"{prefix}:{name}"));
                }
                break;

            case HereSphereTagCategories.Studio:
                if (!string.IsNullOrWhiteSpace(video.Studio?.Name))
                    tags.Add(new HereSphereTag($"{prefix}:{video.Studio.Name}"));
                break;

            case HereSphereTagCategories.Segment:
                AddSegmentTags(tags, prefix, segments);
                break;

            case HereSphereTagCategories.Resolution:
                // Major resolution tier from the primary file's shorter dimension (see ResolveMajorResolutionTier).
                var axis = ResolveResolutionAxis(video, primary);
                if (axis > 0)
                    tags.Add(new HereSphereTag($"{prefix}:{ResolveMajorResolutionTier(axis)}p"));
                break;

            case HereSphereTagCategories.Rating:
                // The viewer's HSP 0-5 rating, only when they have rated the video.
                if (snapshot?.Rating is > 0 && ToHspRating(snapshot.Rating) is > 0 and var hsp)
                    tags.Add(new HereSphereTag($"{prefix}:{hsp.ToString("0.##", CultureInfo.InvariantCulture)}"));
                break;

            case HereSphereTagCategories.Vr:
                tags.Add(new HereSphereTag($"{prefix}:{(video.IsVr ? "TRUE" : "FALSE")}"));
                break;
        }
    }

    /// <summary>Segment markers as timestamped tags on track 0 (chapters above the seek bar).</summary>
    private static void AddSegmentTags(List<HereSphereTag> tags, string prefix, IReadOnlyList<SegmentMarker>? markers)
    {
        foreach (var marker in markers ?? [])
        {
            tags.Add(new HereSphereTag($"{prefix}:{marker.Label}")
            {
                Start = marker.StartMs,
                End = marker.EndMs,
                Track = 0,
            });
        }
    }

    /// <summary>
    /// Loads the timeline markers for a batch of videos from Cove segments, honoring the
    /// configured segment source ("user" = hand-made segments only, "all", or "none"). A segment's
    /// label is its title, falling back to its tag's name; unlabeled segments are skipped. Times go
    /// from seconds to milliseconds; a segment without an end time gets end 0, which HereSphere
    /// reads as "through the end of the video".
    /// <para>
    /// A clip (child video with <c>ParentVideoId</c>) uses its own segments as-is, plus the parent's
    /// segments that overlap the clip window, shifted to the clip's own timeline and trimmed to it.
    /// Cove's code doesn't settle whether clip segments are stored on the clip or on its parent, so
    /// both are handled.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<int, List<SegmentMarker>>> BuildSegmentMarkerMapAsync(
        DbContext db, IReadOnlyCollection<Video> videos, HereSphereRequestSettings settings, CancellationToken ct)
    {
        var map = new Dictionary<int, List<SegmentMarker>>();
        var source = settings.Settings.SegmentSource;
        if (videos.Count == 0 || source == HereSphereSettings.SegmentSources.None)
            return map;

        var hostIds = videos.Select(v => v.Id)
            .Concat(videos.Where(v => v.ParentVideoId is not null).Select(v => v.ParentVideoId!.Value))
            .ToHashSet();

        var query = db.Set<Segment>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Tag)
            .Where(s => s.HostType == SegmentHostType.Video && hostIds.Contains(s.HostId));
        if (source == HereSphereSettings.SegmentSources.User)
            query = query.Where(s => s.SourceKey == "user");

        var byHost = (await query.ToListAsync(ct))
            .GroupBy(s => s.HostId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartSec).ToList());

        foreach (var video in videos)
        {
            var markers = new List<SegmentMarker>();
            if (byHost.TryGetValue(video.Id, out var own))
            {
                foreach (var s in own)
                    AddMarker(markers, s, offset: 0d, windowEnd: null);
            }

            if (video.ParentVideoId is int parentId && byHost.TryGetValue(parentId, out var parentSegments))
            {
                var offset = video.ClipStartSec ?? 0d;
                foreach (var s in parentSegments)
                    AddMarker(markers, s, offset, video.ClipEndSec);
            }

            if (markers.Count > 0)
                map[video.Id] = markers.OrderBy(m => m.StartMs).ToList();
        }
        return map;
    }

    private static void AddMarker(List<SegmentMarker> markers, Segment segment, double offset, double? windowEnd)
    {
        var label = (!string.IsNullOrWhiteSpace(segment.Title) ? segment.Title : segment.Tag?.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(label))
            return;

        // Outside the clip window entirely.
        if (windowEnd is { } we && segment.StartSec >= we)
            return;
        if (segment.EndSec is { } se && se <= offset)
            return;

        var start = Math.Max(0d, segment.StartSec - offset);
        double? end = segment.EndSec is { } e ? e - offset : null;
        if (windowEnd is { } cw)
            end = Math.Min(end ?? cw - offset, cw - offset);

        markers.Add(new SegmentMarker(
            label,
            Math.Round(start * 1000d),
            end is { } ev && ev > start ? Math.Round(ev * 1000d) : 0d));
    }

    /// <summary>
    /// Maps each video id to the names of the groups it belongs to — every static group
    /// (regardless of its "Show in video browsing" flag, which only governs Cove's own browsing
    /// UI) plus, for a resolved user, the built-in per-user dynamic groups (Save for Later, Watch
    /// History, Continue Watching) it currently qualifies for — so video tags can carry a
    /// "2. Group:&lt;name&gt;" value consistent with the catalog's library sections.
    /// </summary>
    private static async Task<Dictionary<int, List<string>>> BuildVideoGroupMapAsync(
        DbContext db, IEnumerable<int> videoIds, int? userId, CancellationToken ct)
    {
        var ids = videoIds.Distinct().ToHashSet();
        var map = new Dictionary<int, List<string>>();
        if (ids.Count == 0)
            return map;

        void Add(int videoId, string name)
        {
            if (!map.TryGetValue(videoId, out var names))
            {
                names = [];
                map[videoId] = names;
            }
            if (!names.Contains(name))
                names.Add(name);
        }

        var groups = await db.Set<Group>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(g => g.Kind == GroupKind.Static)
            .Include(g => g.GroupItems)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Id)
            .ToListAsync(ct);

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                continue;
            foreach (var item in group.GroupItems.OrderBy(i => i.OrderIndex))
            {
                if (item.Kind is not (GroupItemKind.Video or GroupItemKind.VideoRange))
                    continue;
                if (item.VideoId is not int videoId || !ids.Contains(videoId))
                    continue;
                Add(videoId, group.Name);
            }
        }

        if (userId is int resolvedUserId)
        {
            foreach (var (name, groupVideoIds) in await BuildDynamicGroupVideoMapAsync(db, resolvedUserId, ids, ct))
            {
                foreach (var videoId in groupVideoIds)
                    Add(videoId, name);
            }
        }

        return map;
    }

    /// <summary>
    /// Resolves the three built-in per-user dynamic groups Cove derives from engagement data — Save
    /// for Later (<see cref="UserBookmark"/>), Watch History and Continue Watching (both from
    /// <see cref="UserEntityAffinity"/>) — restricted to <paramref name="videoIds"/>, ordered most
    /// recent first. Continue Watching mirrors Cove's own criteria: consumed, a positive resume
    /// position, not yet marked complete, and (when duration is known) not within 5% of the end.
    /// Returns a fixed-order list (even when a group has no matches) so the catalog and tag builder
    /// stay consistent about group name/order.
    /// </summary>
    private static async Task<List<(string Name, List<int> VideoIds)>> BuildDynamicGroupVideoMapAsync(
        DbContext db, int userId, IReadOnlySet<int> videoIds, CancellationToken ct)
    {
        if (videoIds.Count == 0)
            return [("Save for Later", []), ("Watch History", []), ("Continue Watching", [])];

        var saveForLater = await db.Set<UserBookmark>()
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.HostType == AffinityHostType.Video && videoIds.Contains(b.HostId))
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.HostId)
            .ToListAsync(ct);

        var watchHistory = await db.Set<UserEntityAffinity>()
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.HostType == AffinityHostType.Video
                && a.LastConsumedAt != null && videoIds.Contains(a.HostId))
            .OrderByDescending(a => a.LastConsumedAt)
            .Select(a => a.HostId)
            .ToListAsync(ct);

        var continueCandidates = await db.Set<UserEntityAffinity>()
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.HostType == AffinityHostType.Video
                && a.LastConsumedAt != null && a.LastPositionSec > 0 && a.CompleteCount == 0
                && videoIds.Contains(a.HostId))
            .OrderByDescending(a => a.LastConsumedAt)
            .Select(a => new { a.HostId, a.LastPositionSec })
            .ToListAsync(ct);

        var continueWatching = new List<int>();
        if (continueCandidates.Count > 0)
        {
            var candidateIds = continueCandidates.Select(c => c.HostId).ToHashSet();
            var durations = await db.Set<Video>()
                .AsNoTracking()
                .Where(v => candidateIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.MaxDuration, ct);
            foreach (var candidate in continueCandidates)
            {
                var duration = durations.GetValueOrDefault(candidate.HostId);
                if (duration <= 0 || candidate.LastPositionSec < duration * 0.95)
                    continueWatching.Add(candidate.HostId);
            }
        }

        return
        [
            ("Save for Later", saveForLater),
            ("Watch History", watchHistory),
            ("Continue Watching", continueWatching),
        ];
    }
}