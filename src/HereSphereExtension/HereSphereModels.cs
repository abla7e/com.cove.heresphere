using System.Text.Json.Serialization;

namespace HereSphereExtension;

// ===== Requests =====

/// <summary>
/// HereSphere request body. Catalog/auth/scan requests may carry optional credentials, and video
/// data POSTs (rating/favorite write-back) may additionally carry engagement fields.
/// </summary>
public sealed class HereSphereVideoRequest
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>HereSphere signals whether it needs playable media sources; Cove always provides them.</summary>
    [JsonPropertyName("needsMediaSource")]
    public bool? NeedsMediaSource { get; set; }

    /// <summary>HereSphere rating (0-5 scale) to write back for the resolved user.</summary>
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    /// <summary>HereSphere favorite state to write back for the resolved user.</summary>
    [JsonPropertyName("isFavorite")]
    public bool? IsFavorite { get; set; }

    /// <summary>Accepted for compatibility; Cove tags are managed within Cove.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

// ===== Shared value objects =====

/// <summary>A playable file source. HereSphere reads <c>resolution</c> as the frame height.</summary>
public sealed class HereSphereSource
{
    [JsonPropertyName("resolution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Resolution { get; set; }

    [JsonPropertyName("height")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }

    [JsonPropertyName("width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// A HereSphere tag. Whole-video tags carry only <c>name</c>; timestamped tags (Cove segments,
/// emitted as timeline markers) also carry <c>start</c>/<c>end</c> in milliseconds and a
/// <c>track</c> (track 0 renders as chapters above the seek bar).
/// </summary>
public sealed class HereSphereTag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Start { get; set; }

    [JsonPropertyName("end")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? End { get; set; }

    [JsonPropertyName("track")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Track { get; set; }

    public HereSphereTag() { }

    public HereSphereTag(string name) => Name = name;
}

/// <summary>An external subtitle file HereSphere can load alongside the video.</summary>
public sealed class HereSphereSubtitle
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

// ===== Catalog (/heresphere) =====

public sealed class HereSphereCatalogResponse
{
    [JsonPropertyName("access")]
    public int Access { get; set; } = 1;

    [JsonPropertyName("auth-token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthToken { get; set; }

    [JsonPropertyName("banner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HereSphereBanner? Banner { get; set; }

    [JsonPropertyName("library")]
    public List<HereSphereLibrarySection> Library { get; set; } = [];

    [JsonPropertyName("media")]
    public List<HereSphereMediaEntry> Media { get; set; } = [];
}

public sealed class HereSphereBanner
{
    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; set; }

    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }
}

public sealed class HereSphereLibrarySection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Library";

    [JsonPropertyName("list")]
    public List<string> List { get; set; } = [];
}

// ===== Auth (/heresphere/auth) =====

public sealed class HereSphereAuthResponse
{
    [JsonPropertyName("auth-token")]
    public string? AuthToken { get; set; }

    [JsonPropertyName("access")]
    public int Access { get; set; }
}

// ===== Scan (/heresphere/scan) =====

public sealed class HereSphereScanResponse
{
    [JsonPropertyName("scanData")]
    public List<HereSphereScanEntry> ScanData { get; set; } = [];
}

public sealed class HereSphereScanEntry
{
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("dateAdded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DateAdded { get; set; }

    [JsonPropertyName("dateReleased")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DateReleased { get; set; }

    [JsonPropertyName("duration")]
    public long? Duration { get; set; }

    [JsonPropertyName("rating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Rating { get; set; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("tags")]
    public List<HereSphereTag> Tags { get; set; } = [];
}
