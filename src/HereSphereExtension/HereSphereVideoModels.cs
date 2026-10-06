using System.Text.Json.Serialization;

namespace HereSphereExtension;

/// <summary>
/// A single video entry in the catalog's inlined <c>media</c> array. Carries the HereSphere
/// metadata fields (title, dates, duration, VR descriptor, tags) plus the playable <c>sources</c>.
/// </summary>
public sealed class HereSphereMediaEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("thumbnailImage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailImage { get; set; }

    /// <summary>Optional hover-preview video; only set when Cove has a generated preview for the video.</summary>
    [JsonPropertyName("thumbnailVideo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailVideo { get; set; }

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

    [JsonPropertyName("projection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Projection { get; set; }

    [JsonPropertyName("stereo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stereo { get; set; }

    [JsonPropertyName("fov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Fov { get; set; }

    [JsonPropertyName("lens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lens { get; set; }

    [JsonPropertyName("cameraIPD")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CameraIPD { get; set; }

    [JsonPropertyName("isEyeSwapped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsEyeSwapped { get; set; }

    [JsonPropertyName("tags")]
    public List<HereSphereTag> Tags { get; set; } = [];

    [JsonPropertyName("sources")]
    public List<HereSphereSource> Sources { get; set; } = [];
}

/// <summary>
/// Strict HereSphere v1 video-data payload. <c>media</c> holds one or more media tracks; here a
/// single "Original" track carries every playable file as a source.
/// </summary>
public sealed class HereSphereVideoDataResponse
{
    [JsonPropertyName("access")]
    public int Access { get; set; } = 1;

    [JsonPropertyName("auth-token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthToken { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("thumbnailImage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailImage { get; set; }

    /// <summary>Optional hover-preview video; only set when Cove has a generated preview for the video.</summary>
    [JsonPropertyName("thumbnailVideo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbnailVideo { get; set; }

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

    [JsonPropertyName("projection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Projection { get; set; }

    [JsonPropertyName("stereo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stereo { get; set; }

    [JsonPropertyName("fov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Fov { get; set; }

    [JsonPropertyName("lens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lens { get; set; }

    [JsonPropertyName("cameraIPD")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CameraIPD { get; set; }

    [JsonPropertyName("isEyeSwapped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsEyeSwapped { get; set; }

    [JsonPropertyName("writeFavorite")]
    public bool WriteFavorite { get; set; }

    [JsonPropertyName("writeRating")]
    public bool WriteRating { get; set; }

    [JsonPropertyName("tags")]
    public List<HereSphereTag> Tags { get; set; } = [];

    /// <summary>Cove sidecar captions (.srt/.vtt) for the video's files; omitted when there are none.</summary>
    [JsonPropertyName("subtitles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HereSphereSubtitle>? Subtitles { get; set; }

    [JsonPropertyName("media")]
    public List<HereSphereMediaTrack> Media { get; set; } = [];
}

public sealed class HereSphereMediaTrack
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Original";

    [JsonPropertyName("sources")]
    public List<HereSphereSource> Sources { get; set; } = [];
}
