using Cove.Plugins;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HereSphereExtension;

/// <summary>
/// The tag categories HereSphere shows for a video. The order the user configures on the settings
/// page decides each category's number prefix ("1. Tag", "2. Studio", ...), and that prefix is what
/// makes HereSphere sort its tag categories in that order.
/// </summary>
public static class HereSphereTagCategories
{
    public const string Tag = "tag";
    public const string Group = "group";
    public const string Performer = "performer";
    public const string Studio = "studio";
    public const string Segment = "segment";
    public const string Resolution = "resolution";
    public const string Rating = "rating";
    public const string Vr = "vr";

    /// <summary>Default order: the previous fixed layout, with Studio and Segment slotted in.</summary>
    public static readonly IReadOnlyList<string> DefaultOrder =
        [Tag, Group, Performer, Studio, Segment, Resolution, Rating, Vr];

    /// <summary>The label used after the number prefix, e.g. "3. Performer".</summary>
    public static string Label(string key) => key switch
    {
        Tag => "Tag",
        Group => "Group",
        Performer => "Performer",
        Studio => "Studio",
        Segment => "Segment",
        Resolution => "Resolution",
        Rating => "Rating",
        Vr => "VR",
        _ => key,
    };
}

/// <summary>Extension-wide HereSphere settings, persisted in Cove's extension key-value store.</summary>
public sealed class HereSphereSettings
{
    /// <summary>Tag categories in display order. Unknown keys are dropped; missing ones are appended.</summary>
    [JsonPropertyName("tagCategoryOrder")]
    public List<string> TagCategoryOrder { get; set; } = [.. HereSphereTagCategories.DefaultOrder];

    /// <summary>
    /// Which Cove segments become HereSphere timeline markers: <c>"user"</c> (only segments created
    /// by hand in Cove), <c>"all"</c> (every segment, including AI/automatic detections), or
    /// <c>"none"</c>.
    /// </summary>
    [JsonPropertyName("segmentSource")]
    public string SegmentSource { get; set; } = SegmentSources.User;

    /// <summary>Whether Cove sidecar captions are offered to HereSphere as external subtitles.</summary>
    [JsonPropertyName("subtitlesEnabled")]
    public bool SubtitlesEnabled { get; set; } = true;

    public static class SegmentSources
    {
        public const string User = "user";
        public const string All = "all";
        public const string None = "none";
    }

    /// <summary>A complete, duplicate-free category order and a known segment source.</summary>
    public HereSphereSettings Normalize()
    {
        var known = HereSphereTagCategories.DefaultOrder;
        var order = (TagCategoryOrder ?? [])
            .Select(k => k?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(k => known.Contains(k))
            .Distinct()
            .ToList();
        foreach (var key in known)
        {
            if (!order.Contains(key))
                order.Add(key);
        }

        var source = SegmentSource?.Trim().ToLowerInvariant();
        return new HereSphereSettings
        {
            TagCategoryOrder = order,
            SegmentSource = source is SegmentSources.All or SegmentSources.None ? source : SegmentSources.User,
            SubtitlesEnabled = SubtitlesEnabled,
        };
    }
}

/// <summary>A Cove segment converted to a HereSphere timeline marker (times in milliseconds; end 0 = through the end).</summary>
public sealed record SegmentMarker(string Label, double StartMs, double EndMs);

/// <summary>Request-time view of the settings: normalized values plus each category's "N. Label" prefix.</summary>
public sealed class HereSphereRequestSettings
{
    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);

    public HereSphereSettings Settings { get; }

    public HereSphereRequestSettings(HereSphereSettings settings)
    {
        Settings = settings.Normalize();
        for (var i = 0; i < Settings.TagCategoryOrder.Count; i++)
        {
            var key = Settings.TagCategoryOrder[i];
            _prefixes[key] = $"{i + 1}. {HereSphereTagCategories.Label(key)}";
        }
    }

    public static HereSphereRequestSettings Default { get; } = new(new HereSphereSettings());

    /// <summary>The numbered category prefix, e.g. <c>"1. Tag"</c>.</summary>
    public string Prefix(string category)
        => _prefixes.TryGetValue(category, out var p) ? p : HereSphereTagCategories.Label(category);
}

public sealed partial class HereSphereExtension : IStatefulExtension
{
    private const string SettingsKey = "settings";

    private IExtensionStore? _store;
    private HereSphereRequestSettings? _cachedSettings;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    /// <summary>Called by the Cove host with this extension's key-value store (the extension_data table).</summary>
    public void SetStore(IExtensionStore store)
    {
        _store = store;
        _cachedSettings = null;
    }

    /// <summary>
    /// Loads settings, cached in memory after the first read. Falls back to defaults when no store
    /// is available or the stored JSON is unreadable, so a HereSphere request never fails because
    /// of configuration.
    /// </summary>
    private async Task<HereSphereRequestSettings> GetSettingsAsync(CancellationToken ct)
    {
        if (_cachedSettings is { } cached)
            return cached;

        await _settingsLock.WaitAsync(ct);
        try
        {
            if (_cachedSettings is null)
            {
                HereSphereSettings? loaded = null;
                if (_store is not null)
                {
                    try
                    {
                        var json = await _store.GetAsync(SettingsKey, ct);
                        if (!string.IsNullOrWhiteSpace(json))
                            loaded = JsonSerializer.Deserialize<HereSphereSettings>(json);
                    }
                    catch (JsonException)
                    {
                        loaded = null;
                    }
                }
                _cachedSettings = new HereSphereRequestSettings(loaded ?? new HereSphereSettings());
            }
            return _cachedSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>Normalizes, persists and caches new settings; returns the stored (normalized) value.</summary>
    private async Task<HereSphereSettings> SaveSettingsAsync(HereSphereSettings settings, CancellationToken ct)
    {
        if (_store is null)
            throw new InvalidOperationException("The Cove extension store is not available, so settings cannot be saved.");

        var resolved = new HereSphereRequestSettings(settings);
        await _settingsLock.WaitAsync(ct);
        try
        {
            await _store.SetAsync(SettingsKey, JsonSerializer.Serialize(resolved.Settings), ct);
            _cachedSettings = resolved;
        }
        finally
        {
            _settingsLock.Release();
        }
        return resolved.Settings;
    }
}
