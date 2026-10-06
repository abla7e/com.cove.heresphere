# HereSphere Web API — Quick Developer Reference

> **Unofficial community reference** · `HereSphere-JSON-Version: 1` · Updated 2026-08-16  
> For complete schemas, version history, HSP v7 binary serialization, passthrough/chroma-key details, and edge cases, see [`HERESPHERE_API_REFERENCE.md`](HERESPHERE_API_REFERENCE.md).

This is the short version for developers who just want to get a compatible server working.

---

## 1. Core flow

```text
POST /heresphere
    ↓
returns lists of video-data URLs
    ↓
POST /heresphere/scan
    ↓
returns bulk searchable metadata
    ↓
GET/POST <video-data URL>
    ↓
returns metadata; include media sources when playback is requested
    ↓
GET <media URL>
```

Every HereSphere JSON response should include:

```http
HereSphere-JSON-Version: 1
Content-Type: application/json
```

Use **stable absolute video-data URLs**. They act as the video's API identity and do not need to match filenames.

---

## 2. Endpoint cheat sheet

| Endpoint | Purpose |
|---|---|
| `POST /heresphere` | Library discovery. |
| `POST /heresphere/scan` | Bulk search/sort metadata. Recommended. |
| `GET/POST <video-data URL>` | Per-video metadata and playback response. |
| `POST /heresphere/auth` | Optional login → token exchange. |
| `<media URL>` | Serve video bytes; support ranges. |
| `<HSP URL>` | Optional native HereSphere profile. |
| `<eventServer URL>` | Optional playback-event receiver. |

---

## 3. `/heresphere`

```json
{
  "access": 1,
  "library": [
    {
      "name": "Library",
      "list": [
        "https://media.example.com/heresphere/video/123",
        "https://media.example.com/heresphere/video/456"
      ]
    }
  ]
}
```

`access`: `1` = full, `0` = guest/limited, `-1` = bad login.

Optional root banner:

```json
{
  "banner": {
    "image": "https://media.example.com/banner.png",
    "link": "https://media.example.com/"
  }
}
```

---

## 4. `/heresphere/scan`

Implement this to avoid fetching every video's full metadata during library scans.

```json
{
  "scanData": [
    {
      "link": "https://media.example.com/heresphere/video/123",
      "title": "Example VR Video",
      "dateAdded": "2026-08-13",
      "duration": 3600000,
      "rating": 4.5,
      "isFavorite": false,
      "tags": [{ "name": "Studio:Example Studio" }]
    }
  ]
}
```

Useful fields: `link`, `title`, `dateReleased`, `dateAdded`, `duration`, `rating`, `favorites`, `comments`, `isFavorite`, `tags`.

`duration` is always **milliseconds**.

---

## 5. Video-data endpoint

Modern playback requests include:

```json
{
  "needsMediaSource": true
}
```

| Value | Meaning |
|---|---|
| `true` | Playback requested; include `media`. |
| `false` | Metadata only; `media` may be omitted. |
| missing | Assume `true` for old-player compatibility. |

Typical response:

```json
{
  "access": 1,
  "title": "Example VR Video",
  "thumbnailImage": "https://media.example.com/thumb/123.jpg",
  "dateAdded": "2026-08-13",
  "duration": 3600000,
  "projection": "equirectangular",
  "stereo": "sbs",
  "fov": 180,
  "lens": "Linear",
  "tags": [],
  "media": [
    {
      "name": "Original",
      "sources": [
        {
          "resolution": 2880,
          "height": 2880,
          "width": 5760,
          "size": 42000000000,
          "url": "https://media.example.com/video/123.mp4"
        }
      ]
    }
  ]
}
```

---

## 6. Common video fields

| Field | Meaning |
|---|---|
| `title`, `description` | Display metadata. |
| `thumbnailImage` | Static thumbnail URL. |
| `thumbnailVideo` | Optional preview video. |
| `dateReleased`, `dateAdded` | `YYYY-MM-DD`. |
| `duration` | Milliseconds. |
| `rating` | `0.0`–`5.0`. |
| `favorites`, `comments` | Aggregate counts. |
| `isFavorite` | Current user state. |
| `projection`, `stereo`, `fov`, `lens` | VR mapping information. |
| `cameraIPD` | Stereo-camera spacing in centimeters. |
| `isEyeSwapped` | Reverse stereo eye order. |
| `tags` | Searchable/timestamped metadata. |
| `scripts` | Peripheral/funscript URLs. |
| `subtitles` | External subtitle URLs. |
| `media` | Playable variants. |
| `hspArray` | Versioned HSP profiles. |
| `eventServer` | Playback-event receiver. |
| `writeFavorite`, `writeRating`, `writeTags`, `writeHSP` | Allow write-back. |

Unused fields can generally be omitted.

---

## 7. VR format values

### Projection

```text
perspective
equirectangular
equirectangular360
fisheye
cubemap
equiangularCubemap
```

### Stereo

```text
mono
sbs
tb
```

The player also has a newer **Full Side-by-Side** mode internally, but the available API material does not establish a distinct public JSON token for it. Check current developer guidance before inventing one.

### Older documented lens names

```text
Linear
MKX220
MKX200
VRCA220
```

Examples:

```json
{ "projection": "equirectangular", "stereo": "sbs", "fov": 180 }
```

```json
{ "projection": "fisheye", "stereo": "sbs", "fov": 200, "lens": "MKX200" }
```

---

## 8. Media streaming

Support standard HTTP seeking for large VR files:

```text
HEAD
Accept-Ranges: bytes
Range: bytes=...
206 Partial Content
Content-Range
Content-Length
correct Content-Type
```

Do not assume playback is a single sequential download; seeking/buffering can generate many range requests.

**Thumbnail tip:** for VR video, provide a normal-looking 2D thumbnail if possible instead of showing both eyes or raw fisheye imagery.

---

## 9. Tags, scripts, subtitles

Tag:

```json
{
  "name": "Scene:Introduction",
  "start": 0,
  "end": 60000,
  "track": 0,
  "rating": 4.5
}
```

`start`/`end` are milliseconds. `Category:Value` names are recommended, e.g. `Studio:...`, `Talent:...`, `Position:...`, `Scene:...`.

Script:

```json
{
  "scripts": [
    { "name": "Main Script", "url": "https://media.example.com/123.funscript" }
  ]
}
```

Subtitle:

```json
{
  "subtitles": [
    { "name": "English", "language": "English", "url": "https://media.example.com/123.en.srt" }
  ]
}
```

---

## 10. Write-back

Enable only what your server supports:

```json
{
  "writeFavorite": true,
  "writeRating": true,
  "writeTags": true,
  "writeHSP": true
}
```

HereSphere may POST back to the same video-data URL:

```json
{
  "isFavorite": true,
  "rating": 4.5,
  "tags": [],
  "hsp": "BASE64_ENCODED_BINARY_HSP",
  "deleteFile": false
}
```

Treat these as current-state updates, not append-only events.

`deleteFile: true` is destructive. Only honor it if deletion is intentionally supported and properly authorized.

---

## 11. HSP profiles

Preferred:

```json
{
  "hspArray": [
    {
      "url": "https://media.example.com/hsp/123-v7.hsp",
      "version": 7
    }
  ]
}
```

Legacy/deprecated fallback:

```json
{ "hsp": "https://media.example.com/hsp/123.hsp" }
```

Use `hspArray` because HSP versions are not guaranteed to be backward-compatible.

### Standalone HSP wrapper — important

A native standalone HSP exported by HereSphere in August 2026 established that the decompressed standalone file has an outer prefix before the actual v7 media-preset version:

```text
Standalone .hsp after decompression:
int32 1024       standalone outer prefix
int32 7          actual HSP / UMediaPreset version
...              v7 payload

Library.hsl embedded preset:
int32 7          HSP / UMediaPreset version
...              v7 payload
```

**Do not parse `1024` as the HSP version.** External standalone writers must preserve the standalone wrapper; embedded `Library.hsl` records do not contain it.

A controlled native export after changing only Contrast also confirmed that the fourth component is the global color component: `ColorContrast = (1, 1, 1, C)`. The corresponding scalar forms are Saturation `(1,1,1,S)`, Gamma `(1,1,1,G)`, and Offset `(0,0,0,B)`, while per-channel Gain may use `(R,G,B,1)`.

If `writeHSP` is enabled, HereSphere may POST modified HSP bytes as Base64 in `hsp`. The observed write-back uses the same standalone wrapped form.

Recommended handling:

- Base64-decode strictly.
- Enforce a size limit.
- Preserve unknown/future HSP versions byte-for-byte.
- Do not rewrite future versions with an older serializer.
- Keep backups.
- Version/cache-bust HSP URLs when content changes.

Newer HereSphere builds also expose an **Always Load HSP** player option for Web API media.

For the HSP v7 binary layout, standalone-vs-embedded wrapper distinction, image settings, neutral values, compression, and enums, see the full reference.

---

## 12. Playback events

Video response:

```json
{ "eventServer": "https://media.example.com/events" }
```

Example event:

```json
{
  "id": "https://media.example.com/heresphere/video/123",
  "title": "Example VR Video",
  "event": 1,
  "time": 10000.5,
  "speed": 1.0,
  "utc": 1760000000000,
  "connectionKey": "abcd1234"
}
```

Documented event codes:

| Code | Meaning |
|---:|---|
| `0` | open |
| `1` | play |
| `2` | pause |
| `3` | close |

`time` and `utc` are milliseconds.

---

## 13. Optional authentication

```http
POST /heresphere/auth
```

Request:

```json
{ "username": "ExampleUser", "password": "ExamplePass" }
```

Response:

```json
{
  "auth-token": "example-auth-token-12345abcdef",
  "access": 1
}
```

Later requests include:

```http
auth-token: example-auth-token-12345abcdef
```

---

## 14. Newer optional features

You can ignore these unless needed:

| Field | Purpose |
|---|---|
| `alphaPackedSettings` | Alpha-packed passthrough/masking. |
| `chromaKeySettings` | Chroma-key passthrough. |
| `alphaExternal` | External alpha video; publicly documented in v0.12.0+. |
| `handyTokenUrl` | Tokenized script callback. |
| `cameraIPD` | Camera separation in cm. |
| `isEyeSwapped` | Reverse stereo eye order. |

See the full reference for exact schemas and version notes.

---

## 15. Minimal implementation checklist

- [ ] Return `HereSphere-JSON-Version: 1`.
- [ ] Implement `/heresphere`.
- [ ] Use stable absolute video-data URLs.
- [ ] Implement `/heresphere/scan`.
- [ ] Implement per-video GET/POST.
- [ ] Respect `needsMediaSource`.
- [ ] Use milliseconds for time values.
- [ ] Return correct VR format fields when known.
- [ ] Generate useful 2D thumbnails.
- [ ] Support HTTP byte ranges.
- [ ] Prefer `hspArray` over only legacy `hsp`.
- [ ] Keep bulk `/scan` and metadata-only video responses HSP-free; advertise `hspArray` only with an actual playback/media-source request.
- [ ] Default `writeHSP` to `false` unless HSP write-back is specifically required.
- [ ] Avoid replacing/importing an HSP while its target video is actively playing.
- [ ] Advertise only write-back features you support.
- [ ] Protect or disable deletion.
- [ ] Version/cache-bust changing HSP URLs.
- [ ] Use auth/HTTPS outside a trusted LAN.

---

## Full reference

Open [`HERESPHERE_API_REFERENCE.md`](HERESPHERE_API_REFERENCE.md) when you need:

- complete field schemas and examples;
- alpha/chroma-key/passthrough details;
- `alphaExternal` or `handyTokenUrl`;
- player-side overwrite settings;
- HSP v7 binary serialization and compression;
- HSP image-keyframe fields and neutral values;
- enum layouts and primitive serialization rules;
- API evolution/version history;
- compatibility gaps and source/provenance notes.

This quick reference intentionally avoids guessing where the available developer material does not establish an exact value.

## Sparse Image keyframes and advanced native correction

HSP Image data is an array, not a single fixed grade. Multiple complete Image records may be placed at stable timestamps. For automatic generation, use minimum scene duration, sustained before/after evidence, hysteresis, confidence gating, and a hard keyframe cap; do not emit continuous auto-WB keyframes.

The RGB components of Saturation, Contrast, Gamma, and Offset are independent per-channel finishing controls; the fourth component remains global. Keep channel trims small. Primary white balance remains `ColorGain (R,G,B,1)`.

Other useful v7 correction surfaces:

```text
Format       projection/stereo/flip/aspect/zoom/pan/orientation type
Lens         lens names/calibration/FOV
Stitch       shift/scale/shear/flare/slant
Alignment    position/rotation
Orientation  camera rotation
AutoFocus    rotation/focal override/min/max/transition speed
Sync         starting offset/drift per hour
Audio        time offset/volume multiplier
```

Safe automation should preserve horizontal stereo depth, avoid inventing unknown lens coefficients, require multi-frame confidence, keep risky geometry/timing opt-in, and preserve unrelated fields/manual profiles.

