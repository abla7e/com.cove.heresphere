# HereSphere Web API & HSP Format — Community Reference

> **Status:** Unofficial community documentation  
> **Last updated:** 2026-08-16  
> **Web API protocol:** `HereSphere-JSON-Version: 1`  
> **HSP layout covered:** serialized HSP version 7  
> **Validation basis:** 2023 API documentation, 2024 HSP serialization layout, public HereSphere release notes through v0.12.0, static/current-data validation against a Windows Shipping build dated 2025-04-30, and direct byte-level comparison with a native standalone HSP exported by HereSphere in August 2026.

This document is intended as a practical reference for developers implementing a HereSphere-compatible media server or tooling around HereSphere HSP profiles. It consolidates the older developer documentation with later public API additions and current-build observations.

It is **not an official HereSphere LLC specification**. Where a detail is directly documented, currently observed, or only inferred/unknown, that distinction is called out explicitly.

---

## Table of contents

1. [Compatibility and source legend](#1-compatibility-and-source-legend)
2. [Protocol overview](#2-protocol-overview)
3. [Authentication](#3-authentication)
4. [`/heresphere` library endpoint](#4-heresphere-library-endpoint)
5. [`/heresphere/scan` bulk scan endpoint](#5-herespherescan-bulk-scan-endpoint)
6. [Video-data endpoints](#6-video-data-endpoints)
7. [Video metadata fields](#7-video-metadata-fields)
8. [VR projection, stereo, lens, FOV and IPD](#8-vr-projection-stereo-lens-fov-and-ipd)
9. [Media sources and HTTP streaming](#9-media-sources-and-http-streaming)
10. [Thumbnails and previews](#10-thumbnails-and-previews)
11. [Scripts and synchronized peripherals](#11-scripts-and-synchronized-peripherals)
12. [Subtitles](#12-subtitles)
13. [Tags, tracks, ratings and search](#13-tags-tracks-ratings-and-search)
14. [Favorites, ratings and server write-back](#14-favorites-ratings-and-server-write-back)
15. [HSP delivery through the Web API](#15-hsp-delivery-through-the-web-api)
16. [Passthrough, alpha and chroma-key fields](#16-passthrough-alpha-and-chroma-key-fields)
17. [Playback event server](#17-playback-event-server)
18. [Handy/script-token callback](#18-handyscript-token-callback)
19. [Player-side API behavior and overwrite settings](#19-player-side-api-behavior-and-overwrite-settings)
20. [Advanced: HSP v7 binary layout](#20-advanced-hsp-v7-binary-layout)
21. [Advanced: HSP image settings](#21-advanced-hsp-image-settings)
22. [Advanced: HSP enums and serialization conventions](#22-advanced-hsp-enums-and-serialization-conventions)
23. [Implementation recommendations](#23-implementation-recommendations)
24. [API evolution timeline](#24-api-evolution-timeline)
25. [Known gaps and version-sensitive details](#25-known-gaps-and-version-sensitive-details)
26. [Complete end-to-end examples](#26-complete-end-to-end-examples)
27. [Source notes](#27-source-notes)

---

# 1. Compatibility and source legend

This reference uses the following source labels:

| Label | Meaning |
|---|---|
| **D2023** | The supplied Discord document titled **“HereSphere JSON API Version 1”** (2023-era developer documentation). |
| **H2024** | The supplied Discord **HSP serialization layout** (2024), showing `UMediaPreset`, all keyframe structures, version gates and enums through HSP version 7. |
| **B2025** | Current-build observation from an unencrypted Windows Shipping build timestamped **2025-04-30**, plus validation against current saved media-preset data. |
| **N2026** | Direct native-file observation from a standalone `.hsp` exported by HereSphere in **August 2026**, compared byte-for-byte with generated v7 payloads. |
| **Rxxxx** | Public HereSphere release notes from the official HereSphere itch.io devlog. |

### What was confirmed in the April 2025 Windows build

The inspected Windows binary contains the Web API parser/feature names expected from the documented API, including:

- `HereSphere-JSON-Version`
- `auth-token`
- `needsMediaSource`
- `hspArray`
- `writeHSP`
- `writeFavorite`
- `writeRating`
- `writeTags`
- `deleteFile`
- `eventServer`
- `handyTokenUrl`
- `cameraIPD`
- `isEyeSwapped`
- `alphaPackedSettings`
- `chromaKeySettings`

Its Web API parser string table also confirms the projection strings `equirectangular`, `equirectangular360`, `fisheye`, `cubemap`, and `equiangularCubemap`, plus stereo values `sbs` and `tb`. **[B2025]**

### What was added after that inspected PC build

HereSphere v0.12.0 release notes (2026-01-16) publicly document the Web API field `alphaExternal`. Because that field postdates the inspected April 2025 PC binary, it is documented here as a **newer public API feature**, not as something verified in that older PC executable. **[R0.12.0]**

---

# 2. Protocol overview

The HereSphere Web API is a server-driven library protocol. A compatible server exposes a `/heresphere` entry point that returns a library of **video-data URLs**. HereSphere then uses those URLs to retrieve metadata and, when needed, playable media sources.

## 2.1 Required response header

Responses containing HereSphere API JSON should include:

```http
HereSphere-JSON-Version: 1
Content-Type: application/json
```

The presence of `HereSphere-JSON-Version` is what tells the player that the endpoint is a HereSphere API endpoint. **[D2023]**

No public evidence of a JSON API version greater than `1` was found in the supplied documentation or inspected April 2025 PC build.

## 2.2 Entry-point behavior

If a user enters a URL ending in `/heresphere`, HereSphere attempts to open it as the HereSphere Web API without requiring the user to manually switch API mode. **[D2023, R0.7.2]**

Example:

```text
https://media.example.net/heresphere
```

## 2.3 IDs are URLs, not filenames

The library response contains **absolute video-data URLs**. Those URLs are the scene/video identifiers used by the API. Your implementation may choose any stable URL shape, for example:

```text
https://media.example.net/heresphere/video/12345
```

The path after `/heresphere` is implementation-defined. Do not assume the video-data URL must mirror a filesystem path or filename.

## 2.4 Units and common types

| Value | Unit / format |
|---|---|
| `duration` | milliseconds |
| tag `start`, `end` | milliseconds |
| playback event `time` | milliseconds |
| playback event `utc` | milliseconds since Unix epoch |
| `rating` | decimal `0.0`–`5.0` |
| `cameraIPD` | centimeters |
| `fov` | degrees |
| dates | `YYYY-MM-DD` |

---

# 3. Authentication

Authentication is optional. The API supports direct username/password requests and an optional token exchange.

## 3.1 Username/password request body

The root request may contain:

```json
{
  "username": "ExampleUser",
  "password": "ExamplePass"
}
```

Video-data POSTs and scan requests may contain the same credentials. **[D2023]**

## 3.2 Token exchange: `/heresphere/auth`

A server may expose:

```http
POST /heresphere/auth
Content-Type: application/json
```

Request:

```json
{
  "username": "ExampleUser",
  "password": "ExamplePass"
}
```

Response:

```json
{
  "auth-token": "example-auth-token-12345abcdef",
  "access": 1
}
```

HereSphere stores the returned token instead of the password and supplies it on later API requests as the HTTP header:

```http
auth-token: example-auth-token-12345abcdef
```

HTTP header names are case-insensitive. **[D2023]**

## 3.3 `access` values

| Value | Meaning |
|---:|---|
| `1` | member / full access |
| `0` | guest / limited access |
| `-1` | bad login |

A server can use `access: 0` on a video response to provide a trailer or otherwise limited content when the user does not have full access. **[D2023]**

---

# 4. `/heresphere` library endpoint

The library endpoint is the discovery root.

## 4.1 Request

Historically documented behavior is a POST carrying authentication data:

```http
POST /heresphere
Content-Type: application/json
```

```json
{
  "username": "ExampleUser",
  "password": "ExamplePass"
}
```

Servers commonly also support GET for convenience, but POST is the documented form. **[D2023]**

## 4.2 Response

```json
{
  "access": 1,
  "banner": {
    "image": "https://media.example.net/banner.png",
    "link": "https://media.example.net/"
  },
  "library": [
    {
      "name": "Recent",
      "list": [
        "https://media.example.net/heresphere/video/123",
        "https://media.example.net/heresphere/video/456"
      ]
    },
    {
      "name": "Favorites",
      "list": [
        "https://media.example.net/heresphere/video/456"
      ]
    }
  ]
}
```

### Root fields

| Field | Type | Required? | Description |
|---|---|---|---|
| `access` | integer | yes | Current user access state: `1`, `0`, or `-1`. |
| `banner` | object | no | Optional clickable banner. |
| `banner.image` | URL string | if banner | Banner image. The older API doc recommends max `1920×200`. |
| `banner.link` | URL string | if banner | Destination when the banner is clicked. |
| `library` | array | yes | Named lists of video-data URLs. |
| `library[].name` | string | yes | Display name for the list/tab. |
| `library[].list` | URL array | yes | Absolute video-data URLs. |

A video URL may appear in more than one list. List order is preserved and is useful for “Recent”, “Recommended”, “Favorites”, studios, collections, etc. **[D2023]**

## 4.3 Scan behavior without `/heresphere/scan`

If a server does not provide the bulk scan endpoint, the player may issue rapid GET requests to every video-data URL to obtain metadata for the library UI and search index. Implementations should be prepared for a burst of requests and should avoid bot/rate-limit rules that block the player. **[D2023]**

For large libraries, implement `/heresphere/scan`.

---

# 5. `/heresphere/scan` bulk scan endpoint

Added in v0.7.3, this endpoint reduces the number of per-video requests required when HereSphere scans the library. **[D2023, R0.7.3]**

## 5.1 Request

```http
POST /heresphere/scan
HereSphere-JSON-Version: 1
Content-Type: application/json
```

Authentication follows the same rules as other API requests.

## 5.2 Response

```json
{
  "scanData": [
    {
      "link": "https://media.example.net/heresphere/video/123",
      "title": "Example Title",
      "dateReleased": "2026-01-02",
      "dateAdded": "2026-01-03",
      "duration": 3600000.0,
      "rating": 4.5,
      "favorites": 23,
      "comments": 12,
      "isFavorite": true,
      "tags": [
        {
          "name": "Studio:Example Studio",
          "start": 0.0,
          "end": 0.0,
          "track": 2,
          "rating": 4.5
        }
      ]
    }
  ]
}
```

### `scanData` fields

| Field | Type | Description |
|---|---|---|
| `link` | URL string | Video-data URL; must correspond to a library item. |
| `title` | string | Video title. |
| `dateReleased` | `YYYY-MM-DD` | Release date. |
| `dateAdded` | `YYYY-MM-DD` | Date added to server/library. |
| `duration` | number | Duration in milliseconds. |
| `rating` | number | `0`–`5`. |
| `favorites` | integer | Favorite count. |
| `comments` | integer | Comment count. |
| `isFavorite` | boolean | Current user’s favorite state. |
| `tags` | array | Timestamped/categorized tags. |

The purpose of this response is **search/sort/index metadata**, not full playback data. Do not include large or expensive media-source data here unless there is a specific reason. **[D2023, R0.7.3]**

Starting with v0.11.9, HereSphere added duration sorting in the media library and Web API view, making accurate `duration` values more useful. **[R0.11.9]**

---

# 6. Video-data endpoints

Every URL in `library[].list` is a video-data endpoint.

## 6.1 GET: metadata/library population

HereSphere may GET video-data URLs while building the library. A server should return the metadata necessary to render the library item.

## 6.2 POST: playback and state synchronization

When the user attempts to play a video, HereSphere sends a POST. Modern requests include:

```json
{
  "username": "ExampleUser",
  "password": "ExamplePass",
  "needsMediaSource": true
}
```

If token authentication is in use, `auth-token` is sent as a header instead of relying on a stored password. **[D2023]**

## 6.3 `needsMediaSource`

Added in v0.7.3. **[R0.7.3]**

- `true`: the user is attempting playback; actual media source URLs are needed.
- `false`: HereSphere only needs metadata for UI/library purposes; the server can omit expensive media source generation.
- missing: **assume `true`** for backward compatibility with older HereSphere versions.

This field is one of the most useful performance optimizations in the API.

---

# 7. Video metadata fields

A full video response can contain the following fields.

```json
{
  "access": 1,
  "title": "Example Title",
  "description": "Example description",
  "thumbnailImage": "https://media.example.net/thumbs/123.jpg",
  "thumbnailVideo": "https://media.example.net/previews/123.mp4",
  "dateReleased": "2026-01-02",
  "dateAdded": "2026-01-03",
  "duration": 3600000.0,
  "rating": 4.5,
  "favorites": 23,
  "comments": 12,
  "isFavorite": true,
  "projection": "equirectangular",
  "stereo": "sbs",
  "isEyeSwapped": false,
  "fov": 180.0,
  "lens": "Linear",
  "cameraIPD": 6.5,
  "eventServer": "https://media.example.net/events/123",
  "scripts": [],
  "subtitles": [],
  "tags": [],
  "media": [],
  "writeFavorite": false,
  "writeRating": false,
  "writeTags": false,
  "writeHSP": false
}
```

### Field reference

| Field | Type | Description |
|---|---|---|
| `access` | integer | `1` full access, `0` limited/guest, `-1` bad login. |
| `title` | string | Display title. |
| `description` | string | Description text. |
| `thumbnailImage` | URL string | Static thumbnail. |
| `thumbnailVideo` | URL string | Hover/preview video. |
| `dateReleased` | date string | `YYYY-MM-DD`. |
| `dateAdded` | date string | `YYYY-MM-DD`. |
| `duration` | number | Milliseconds. |
| `rating` | number | `0`–`5`. |
| `favorites` | integer | Aggregate favorite count. |
| `comments` | integer | Aggregate comment count. |
| `isFavorite` | boolean | Current user favorite state. |
| `projection` | string | Projection type. See section 8. |
| `stereo` | string | Stereo layout. See section 8. |
| `isEyeSwapped` | boolean | Swap left/right (or top/bottom) eye order. |
| `fov` | number | Actual field of view in degrees. |
| `lens` | string | Lens/calibration preset. |
| `cameraIPD` | number | Stereo camera separation in cm. Added in v0.7.2. |
| `alphaPackedSettings` | object | Optional alpha-packed passthrough settings. |
| `chromaKeySettings` | object | Optional chroma-key passthrough settings. |
| `alphaExternal` | URL string | External alpha video. Added publicly in v0.12.0. |
| `hspArray` | array | Versioned HSP URLs. Preferred over legacy `hsp`. |
| `hsp` | URL string | Deprecated single HSP URL fallback. |
| `eventServer` | URL string | Playback event POST destination. |
| `handyTokenUrl` | URL string | Tokenized script callback. Added in v0.11.3. |
| `scripts` | array | Synchronized-peripheral scripts. |
| `subtitles` | array | External subtitles. |
| `tags` | array | Categorized/timestamped tags. |
| `media` | array | Encodings and playable sources. |
| `writeFavorite` | boolean | Allow favorite write-back. |
| `writeRating` | boolean | Allow rating write-back. |
| `writeTags` | boolean | Allow tag write-back. |
| `writeHSP` | boolean | Allow HSP write-back. |

Fields that are not relevant may generally be omitted; older examples often included empty strings/arrays. Servers should prefer valid values over placeholders when possible.

---

# 8. VR projection, stereo, lens, FOV and IPD

These fields let the server describe how the image should be mapped in VR.

## 8.1 `projection`

The 2023 API document lists:

| String | Meaning |
|---|---|
| `perspective` | Flat/perspective projection. |
| `equirectangular` | VR equirectangular projection, commonly 180°. |
| `equirectangular360` | 360° equirectangular. |
| `fisheye` | Fisheye/lens-based projection. |
| `cubemap` | Cubemap. |
| `equiangularCubemap` | Equiangular cubemap. |

The April 2025 PC binary contains parser strings for all non-default projection names above. `Perspective` is also present in the internal projection enum. **[D2023, B2025, H2024]**

## 8.2 `stereo`

Documented Web API strings:

| String | Meaning |
|---|---|
| `mono` | Monoscopic. |
| `sbs` | Side-by-side stereo. |
| `tb` | Top-bottom stereo. |

The player added a **Full Side-by-Side** stereo mode in v0.11.3 and the HSP v7 enum includes `FullSideBySide`. Filename detection examples include `_LRF` and `_SBSF`. **[R0.11.3, H2024]**

**Important compatibility note:** the inspected April 2025 Web API parser string table exposes `sbs` and `tb` but does not expose a distinct Full-SBS JSON token next to them. The exact Web API string for Full Side-by-Side is therefore **not established by the supplied API document or this binary inspection**. Do not invent a value; verify against current Discord developer documentation/player behavior before relying on a Full-SBS JSON string. **[B2025]**

## 8.3 `isEyeSwapped`

Boolean. For stereo video, this reverses the default eye order.

```json
{
  "isEyeSwapped": true
}
```

## 8.4 `fov`

Decimal field of view in degrees:

```json
{
  "fov": 180.0
}
```

## 8.5 `lens`

The 2023 API doc lists:

- `Linear`
- `MKX220`
- `MKX200`
- `VRCA220`

The inspected 2025 PC build also contains friendly names for these lens types. **[D2023, B2025]**

## 8.6 `cameraIPD`

Added in v0.7.2. It is the separation between the stereo cameras in **centimeters**. The release notes/documentation describe `6.0`–`7.0 cm` as a typical range and `6.4`–`6.5` as a useful default. **[D2023, R0.7.2]**

```json
{
  "cameraIPD": 6.5
}
```

---

# 9. Media sources and HTTP streaming

`media` is an array of encodings/variants. Each encoding has a name and one or more sources.

```json
{
  "media": [
    {
      "name": "h265",
      "sources": [
        {
          "resolution": 2880,
          "height": 2880,
          "width": 5760,
          "size": 42000000000,
          "url": "https://cdn.example.net/video/123-5760x2880.mp4"
        }
      ]
    }
  ]
}
```

## 9.1 Encoding object

| Field | Type | Description |
|---|---|---|
| `name` | string | Human-readable encoding name; codec/resolution labels are common. |
| `sources` | array | Available source variants. |

## 9.2 Source object

| Field | Type | Description |
|---|---|---|
| `resolution` | integer | Resolution classification. Historically usually the video height; top-bottom content may use an adjusted class. |
| `height` | integer | Actual encoded pixel height. |
| `width` | integer | Actual encoded pixel width. |
| `size` | integer | File size in bytes. |
| `url` | URL string | Playable video URL. |

The inspected PC binary also contains a `stream` key in its media-source parser area, but its authoritative semantics are not described by the supplied Web API document. Omit undocumented fields unless you are following newer official/Discord guidance. **[B2025]**

## 9.3 HTTP behavior strongly recommended for media URLs

For large VR media, a practical media server should support:

- `HEAD`
- `Accept-Ranges: bytes`
- single byte-range requests
- `206 Partial Content`
- `Content-Range`
- accurate `Content-Length`
- stable `ETag` and/or `Last-Modified`
- correct video MIME type

This is normal HTTP streaming practice and allows efficient seeking without downloading the entire file. HereSphere has used modern network stacks including HTTP/2/HTTP/3-capable Cronet on Quest since v0.9.5. **[R0.9.5]**

Do not assume the player will fetch a source only once; seeking and buffering can create multiple range requests.

---

# 10. Thumbnails and previews

## 10.1 Static image

```json
{
  "thumbnailImage": "https://media.example.net/thumb/123.jpg"
}
```

Use a normal 2D browse image. For stereo/fisheye VR source material, a server-side generated single-eye/dewarped thumbnail generally provides a much better library experience than squeezing both eyes into one card.

## 10.2 Hover/preview video

```json
{
  "thumbnailVideo": "https://media.example.net/preview/123.mp4"
}
```

HereSphere can play the preview when the item is hovered/focused. Public release notes have included fixes around pausing Web API preview videos when menus are hidden, confirming this behavior. **[R0.9.5, R0.11.0]**

---

# 11. Scripts and synchronized peripherals

The `scripts` array provides synchronized-peripheral scripts such as funscripts.

```json
{
  "scripts": [
    {
      "name": "Main Script",
      "url": "https://media.example.net/scripts/123.funscript",
      "rating": 4.8
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `name` | string | Display name. |
| `url` | URL string | Script download URL. |
| `rating` | number | Optional script rating, `0`–`5`. |

HereSphere can expose a script selector when multiple scripts are attached. **[D2023, R0.7.2]**

### Multipart media and scripts

v0.7.3 notes that when a multipart video has multiple funscripts, the encoding index is used to automatically load the corresponding script; servers should keep the script count aligned with multipart encodings (using placeholders when necessary). **[R0.7.3]**

---

# 12. Subtitles

External subtitles were added to HereSphere in v0.8.0 and are represented in the Web API as:

```json
{
  "subtitles": [
    {
      "name": "English",
      "language": "English",
      "url": "https://media.example.net/subtitles/123.en.srt"
    },
    {
      "name": "Japanese",
      "language": "Japanese",
      "url": "https://media.example.net/subtitles/123.ja.ass"
    }
  ]
}
```

The 2023 documentation explicitly mentions `.srt`, `.ssa`, and `.ass`. **[D2023, R0.8.0]**

Use the correct MIME type if practical, but the file extension is important for compatibility.

---

# 13. Tags, tracks, ratings and search

Tags can describe the whole video or timestamped ranges.

```json
{
  "tags": [
    {
      "name": "Scene:Introduction",
      "start": 0.0,
      "end": 60000.0,
      "track": 0,
      "rating": 2.5
    },
    {
      "name": "Talent:Example Name",
      "start": 0.0,
      "end": 0.0,
      "track": 1,
      "rating": 5.0
    }
  ]
}
```

## 13.1 Tag fields

| Field | Type | Required? | Description |
|---|---|---|---|
| `name` | string | yes | Tag text, optionally `Category:Tag`. |
| `start` | number | no | Start time in ms. |
| `end` | number | no | End time in ms. `0` can mean “through video duration”. |
| `track` | integer | no | Visual/tag track. |
| `rating` | number | no | Tag rating. |

## 13.2 Categories

A colon separates category and value:

```text
Studio:Example Studio
Talent:Example Name
Position:Standing
```

Without a colon, the tag appears under an uncategorized grouping. **[D2023]**

The older docs recommend common categories such as:

- `Position`
- `Talent`
- `Studio`

These are conventions, not a closed enum.

## 13.3 Tracks

Timestamped tags can be assigned to tracks. Track `0` is particularly useful for scene chapters above the seekbar. Later releases expanded visibility of the first and second tracks. **[D2023, R0.9.3]**

## 13.4 Search/autoseek

HereSphere can combine tag criteria and ratings to search/filter content and automatically seek to ranges that match user criteria. This makes accurate categorized/timestamped tags much more useful than simple flat keywords. **[D2023]**

---

# 14. Favorites, ratings and server write-back

A server controls which user changes HereSphere is allowed to POST back by setting:

```json
{
  "writeFavorite": true,
  "writeRating": true,
  "writeTags": true,
  "writeHSP": true
}
```

When enabled, HereSphere may POST updates to the **same video-data URL**. **[D2023, R0.7.2]**

## 14.1 Example write-back request

```json
{
  "username": "ExampleUser",
  "password": "ExamplePassword",
  "isFavorite": true,
  "rating": 4.5,
  "tags": [
    {
      "name": "Scene:Introduction",
      "start": 0.0,
      "end": 60000.0,
      "track": 0,
      "rating": 2.5
    }
  ],
  "hsp": "BASE64_ENCODED_BINARY_HSP",
  "deleteFile": false
}
```

With token auth, the `auth-token` header can be used instead of depending on a password in the body.

## 14.2 Write-back fields

| Incoming field | Meaning |
|---|---|
| `isFavorite` | New favorite state. |
| `rating` | New video rating. |
| `tags` | Current edited tag list. |
| `hsp` | Base64-encoded binary HSP data. |
| `deleteFile` | User requests permanent file deletion. |

### Deletion warning

`deleteFile: true` is a destructive request. A server should only honor it if deletion is an explicit product feature, the user is authenticated/authorized, and the implementation has appropriate safety/confirmation policy. It is valid to ignore or disable deletion.

### Treat write-backs as authoritative state updates

Do not append duplicate tags on every POST. Prefer idempotent “replace/update current state” semantics unless your application intentionally implements history.

---

# 15. HSP delivery through the Web API

HSP files contain HereSphere media settings/keyframes and tags in a compressed binary format.

## 15.1 Preferred: `hspArray`

Added in v0.10.0 to handle HSP version compatibility. **[D2023, R0.10.0]**

```json
{
  "hspArray": [
    {
      "url": "https://media.example.net/hsp/123-v6.hsp",
      "version": 6
    },
    {
      "url": "https://media.example.net/hsp/123-v7.hsp",
      "version": 7
    }
  ]
}
```

The `version` exists because newer HSP files are not necessarily backward-compatible with older HereSphere installations. The player can select a version it understands. **[D2023, R0.10.0]**

### Observed browse-time safety issue (August 2026)

During current-build testing, HereSphere was observed to terminate with native UE4 fatal errors while merely browsing a Web API library whose per-video metadata responses advertised HSP profiles. The failures included `FMemoryWriter does not support data larger than 2GB` and `BulkData compressed header read error`. They were reproduced without active video playback. The exact internal player fault is not established, so this should not be read as a claim that every `hspArray` metadata response will crash HereSphere.

A safer server contract is therefore:

- bulk `/scan` and ordinary metadata-only video requests return metadata only;
- do **not** advertise `hsp`/`hspArray` merely because the library browser asks for a video's details;
- provide the selected HSP only when HereSphere requests an actual playback media source (for example a POST whose `needsMediaSource` resolves true);
- prefer `writeHSP: false` unless write-back is explicitly required;
- avoid replacing/importing an HSP while the same video is actively playing.

This recommendation intentionally separates *observed failure*, *unknown internal cause*, and *defensive server behavior*.

## 15.2 Legacy fallback: `hsp`

```json
{
  "hsp": "https://media.example.net/hsp/123.hsp"
}
```

The 2023 developer doc marks this field as deprecated in favor of `hspArray`. **[D2023]**

## 15.3 `writeHSP`

When `writeHSP` is `true`, HereSphere may POST a modified HSP back as Base64 in the `hsp` request field. Servers should:

1. Base64-decode strictly.
2. Enforce a sane size limit.
3. Preserve the original bytes unless the format/version is understood.
4. Avoid rewriting future HSP versions through an older serializer.
5. Keep backups if overwriting user-created settings.

A native August 2026 write-back/export confirmed that the Base64 payload is the same **standalone HSP container form** described in §20.1a/§22.3 below: after decompression, it begins with an outer `int32 1024`, followed by the actual inner HSP/`UMediaPreset` version (`int32 7` in the observed file). Do **not** interpret the first decompressed integer of a standalone HSP as the HSP version. **[N2026]**

## 15.4 Player “Always Load HSP” option

v0.11.4 added a player option to **always load HSP files when using the HereSphere Web API**. This is useful for servers that want their supplied HSP profile to be applied consistently instead of relying on previously cached/local media settings. **[R0.11.4]**

## 15.5 Cache invalidation

If a server changes the contents at an HSP URL, use normal HTTP cache validators or a revisioned URL/query string. Stable content-addressed or revisioned HSP URLs avoid stale profiles in player/network caches.

---

# 16. Passthrough, alpha and chroma-key fields

## 16.1 `alphaPackedSettings`

If present, the older API contract says the player switches to passthrough background and alpha-packed masking. **[D2023, R0.10.0]**

```json
{
  "alphaPackedSettings": {
    "shiftX": 0.25,
    "shiftY": -0.5,
    "scaleX": 0.4,
    "scaleY": 0.4,
    "opacityMultiplier": 1.0
  }
}
```

Documented 2023 defaults:

| Field | Default |
|---|---:|
| `shiftX` | `0.25` |
| `shiftY` | `-0.5` |
| `scaleX` | `0.4` |
| `scaleY` | `0.4` |
| `opacityMultiplier` | `1.0` |

### Version note on opacity

The v0.12.0 release notes say the player’s **default opacity multiplier was increased to `2.0`**. That is newer than the 2023 API document’s default of `1.0`. If exact behavior matters, send an explicit `opacityMultiplier` rather than relying on a version-dependent default. **[D2023, R0.12.0]**

## 16.2 `chromaKeySettings`

If present, the older API contract says the player switches to passthrough and chroma-key masking. **[D2023, R0.10.0]**

```json
{
  "chromaKeySettings": {
    "red1": 0.0,
    "green1": 0.0,
    "blue1": 0.0,
    "cutoff1": 0.1,
    "soften1": 0.0,
    "exponent1": 1.0,
    "spillMask1": 1.0,

    "red2": 0.0,
    "green2": 0.0,
    "blue2": 0.0,
    "cutoff2": 0.1,
    "soften2": 0.0,
    "exponent2": 1.0,
    "spillMask2": 1.0,

    "red3": 0.0,
    "green3": 0.0,
    "blue3": 0.0,
    "cutoff3": 0.1,
    "soften3": 0.0,
    "exponent3": 1.0,
    "spillMask3": 1.0,

    "despillRed": 0.0,
    "despillGreen": 0.0,
    "despillBlue": 0.0,
    "despillCutoff": 0.2,
    "despillSoften": 0.2,
    "despillExponent": 1.0,
    "despillMask": 0.0,
    "despillStrength": 1.0,

    "injectRed": 0.5,
    "injectGreen": 0.5,
    "injectBlue": 0.5,
    "injectStrength": 1.0,
    "opacityMultiplier": 1.0
  }
}
```

All fields are optional according to the 2023 document; omitted fields use player defaults. **[D2023]**

## 16.3 `alphaExternal` — v0.12.0+

v0.12.0 publicly added an external alpha-video URL:

```json
{
  "alphaExternal": "https://media.example.net/alpha/123_XALPHA.mp4"
}
```

The alpha video controls opacity for passthrough. The release notes recommend matching keyframe spacing with the main video so seeks remain aligned. **[R0.12.0]**

This field is **newer than the April 2025 PC binary inspected for this document**, so servers targeting mixed player versions should treat it as version-sensitive.

---

# 17. Playback event server

Added in v0.7.4. A video response can include:

```json
{
  "eventServer": "https://media.example.net/events"
}
```

HereSphere POSTs playback events to that URL. **[D2023, R0.7.4]**

## 17.1 Documented event payload

```json
{
  "username": "ExampleUser",
  "id": "https://media.example.net/heresphere/video/123",
  "title": "Example Video Title",
  "event": 1,
  "time": 10000.5,
  "speed": 1.0,
  "utc": 1760000000000.0,
  "connectionKey": "abcd1234"
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `username` | string | Current username. |
| `id` | string/URL | HereSphere video-data URL/identifier. |
| `title` | string | Video title. |
| `event` | integer | Playback event code. |
| `time` | number | Playback time in ms. |
| `speed` | number | Playback speed multiplier. |
| `utc` | number | Milliseconds since Unix epoch. |
| `connectionKey` | string | Synchronized-peripheral connection key. |

### Event values documented in the 2023 API document

| Value | Meaning |
|---:|---|
| `0` | open |
| `1` | play |
| `2` | pause |
| `3` | close |

Seek and playback-speed changes are sent using a play event in that document. **[D2023]**

## 17.2 Time-offset behavior

v0.9.3 changed the event server so the synchronized-peripherals time offset is applied to reported event time, with a minimum reported playback time of zero. **[R0.9.3]**

## 17.3 Buffering/suspended behavior

v0.11.0 release notes state that buffering now correctly triggers playback suspended/resumed behavior for synchronized peripherals **and the event server**. However, the supplied 2023 API document does not define numeric event codes for suspended/resumed messages. This reference intentionally does **not** invent those numeric values. **[R0.11.0]**

If your integration depends on those events, capture traffic from the player version you target or consult current Discord developer documentation.

---

# 18. Handy/script-token callback

v0.11.3 added `handyTokenUrl` to video JSON. **[R0.11.3, B2025]**

```json
{
  "handyTokenUrl": "https://media.example.net/handy/token/123"
}
```

When HereSphere needs a tokenized script URL, it POSTs:

```json
{
  "connectionKey": "abcd1234"
}
```

The endpoint responds with:

```json
{
  "scriptUrl": "https://media.example.net/scripts/123.funscript?token=..."
}
```

This lets the server generate a script download URL tied to the Handy connection key. **[R0.11.3]**

---

# 19. Player-side API behavior and overwrite settings

These are **player settings**, not JSON fields, but they strongly affect server behavior.

## 19.1 API overwrite controls

v0.7.2 documented API overwrite toggles for favorite/rating behavior. The inspected April 2025 PC binary contains API overwrite state/functions for:

- favorite
- rating
- tags

When an overwrite option is enabled, server-supplied API state can replace the player’s locally retained state for that property. If a server does not support write-back but users want local edits to persist, the corresponding overwrite option may need to be disabled. **[R0.7.2, B2025]**

## 19.2 Always Load HSP

Added in v0.11.4. When enabled, Web API HSPs are always loaded for Web API videos. This is useful when the server intentionally owns the current HSP profile. **[R0.11.4]**

## 19.3 Auto Update/Create HSP

HereSphere has separate local user settings for automatically updating/creating HSP files (publicly described by v0.9.5). These local HSP behaviors are distinct from the Web API’s `writeHSP` flag. **[R0.9.5]**

---

# 20. Advanced: HSP v7 binary layout

This section is for developers building HSP parsers/writers. It is not required to implement the JSON API if you simply serve HSP files exported by HereSphere.

The 2024 layout defines a serialized `UMediaPreset` and its keyframe groups. Current saved-media data from the inspected April 2025 Windows build was successfully parsed as **HSP version 7**, with 2,832 valid media-preset records in the validation dataset. **[H2024, B2025]**

Future versions may add fields; version-aware parsing is mandatory.

## 20.1a Standalone `.hsp` wrapper vs embedded `Library.hsl` preset

Direct comparison with a native standalone HSP exported by HereSphere in August 2026 established an important container distinction that is **not visible when parsing media presets embedded inside `Library.hsl`**. **[N2026]**

After the normal Unreal compressed-proxy envelope is decompressed, the observed standalone `.hsp` begins with:

```text
int32 1024       observed standalone outer prefix
int32 7          actual inner HSP / UMediaPreset version
...              serialized UMediaPreset v7 payload
```

By contrast, the 2,832 valid media presets inspected inside `Library.hsl` begin directly with the inner version field:

```text
int32 7          HSP / UMediaPreset version
...              serialized UMediaPreset v7 payload
```

This distinction matters for external writers. A serializer can be perfectly correct for the **inner** v7 `UMediaPreset` payload and still produce a standalone `.hsp` that HereSphere downloads/imports but does not apply if the outer standalone prefix is omitted. That exact failure mode was reproduced and resolved during validation. **[B2025, N2026]**

Practical parser guidance:

- For a standalone `.hsp`, decompress the Unreal proxy envelope first.
- If the first two little-endian `int32` values are `1024, 7` in the observed v7 form, treat `1024` as the standalone outer prefix and `7` as the HSP version.
- Do not report the standalone file as “HSP version 1024.”
- For presets embedded in `Library.hsl`, do not require or synthesize the standalone prefix.
- Preserve unknown future wrapper/version combinations byte-for-byte unless they are independently understood.

## 20.1 Top-level `UMediaPreset`

Serialization order:

| Order | Field | Type | Version condition |
|---:|---|---|---|
| 1 | `Version` | int32 | always |
| 2 | `UniqueID` | FString | always |
| 3 | `Title` | FString | always |
| 4 | `Description` | FString | always |
| 5 | `DateAdded` | FDateTime | always |
| 6 | `DateReleased` | FDateTime | v5+; older versions use `DateAdded` |
| 7 | `DateLastPlayed` | FDateTime | always |
| 8 | `DateEdited` | FDateTime | always |
| 9 | `Duration` | FTimespan | always |
| 10 | `ResumeTime` | FTimespan | always |
| 11 | `ABLoopStartTime` | FTimespan | v7+ |
| 12 | `ABLoopEndTime` | FTimespan | v7+ |
| 13 | `PlayCount` | int32 | always |
| 14 | `Comments` | int32 | always |
| 15 | `Favorites` | int32 | always |
| 16 | `bIsFavorite` | bool | always |
| 17 | `AverageRating` | float | always |
| 18 | `AudioTrack` | int32 | v4+ |
| 19 | HSP tag count + tags | array | always; tag `Track` is v3+ |
| 20 | `ProjectionKeyFrames` | struct | always |

**[H2024]**

### HSP tag

Each `FHSTag` serializes:

1. `Tag` — FString
2. `Rating` — float
3. `Start` — FTimespan
4. `End` — FTimespan
5. `Track` — int32, v3+

For pre-v3 profiles, track indices are reconstructed when loaded. **[H2024]**

## 20.2 Projection keyframe-group order

`FProjectionKeyFrames` serializes groups in this exact order:

1. Format
2. Lens
3. Stitch
4. Alignment
5. Orientation
6. Origin
7. Motion
8. AutoFocus
9. Sync
10. Transition
11. Image
12. Audio — v3+
13. Environment — v7+

Each group is serialized as an int32 count followed by that many keyframe records. **[H2024]**

## 20.3 Format keyframe

`FFormatKeyFrame`:

| Field | Type | Condition |
|---|---|---|
| `Time` | FTimespan | always |
| `ProjectionMode` | enum/uint8 | always |
| `StereoMode` | enum/uint8 | always |
| `bEyeSwapEnabled` | bool | always |
| `bForceMonoEnabled` | bool | v6+ |
| `bFlipFrontBack` | bool | v7+ |
| `bFlipLeftRight` | bool | v7+ |
| `bFlipUpDown` | bool | v7+ |
| `AspectRatioType` | enum/uint8 | always |
| `AspectRatio` | float | always |
| `Zoom` | FVector2D | always |
| `Pan` | FVector2D | always |
| `OrientationType` | enum/uint8 | v1+ |

## 20.4 Lens keyframe

`FLensKeyFrame`:

1. `Time` — FTimespan
2. `TrueLensName` — FString
3. `ExportLensName` — FString
4. `TrueLensCalibration` — FLinearColor (4 floats)
5. `ExportLensCalibration` — FLinearColor (4 floats)
6. `TrueFOV` — float
7. `ExportFOV` — float

## 20.5 Stitch keyframe

`FStitchKeyFrame`:

1. `Time` — FTimespan
2. `Shift` — FVector4
3. `Scale` — FVector4
4. `Shear` — FVector4
5. `Flare` — FVector4
6. `Slant` — FVector4

## 20.6 Alignment keyframe

`FAlignmentKeyFrame`:

1. `Time` — FTimespan
2. `Position` — FVector (3 floats)
3. `Rotation` — FRotator (3 floats)

## 20.7 Orientation keyframe

`FOrientationKeyFrame`:

1. `Time` — FTimespan
2. `Orientation` — FRotator

## 20.8 Origin keyframe

`FOriginKeyFrame`:

1. `Time` — FTimespan
2. `Origin` — FVector

## 20.9 Motion keyframe

`FMotionKeyFrame`:

1. `Time` — FTimespan
2. `MotionDistance` — float

## 20.10 AutoFocus keyframe

`FAutoFocusKeyFrame`:

| Field | Type | Condition |
|---|---|---|
| `Time` | FTimespan | always |
| `Rotation` | FRotator | always |
| `FocalDistanceOverride` | float | always |
| `MinFocusDistance` | float | v2+ |
| `MaxFocusDistance` | float | v2+ |
| `FocusTransitionSpeed` | float | v2+ |

## 20.11 Sync keyframe

`FSyncKeyFrame`:

1. `Time` — FTimespan
2. `StartingTicksOffset` — FTimespan
3. `TicksPerHourOffset` — FTimespan

## 20.12 Transition keyframe

`FTransitionKeyFrame`:

1. `Time` — FTimespan
2. `Type` — `EKeyFrameTransitionType` enum
3. `Duration` — FTimespan

The supplied 2024 layout references `EKeyFrameTransitionType` but does not include its enum definition. Do not hard-code guessed numeric meanings without another authoritative source. **[H2024]**

## 20.13 Audio keyframe — v3+

`FAudioKeyFrame`:

1. `Time` — FTimespan
2. `TimeOffset` — float
3. `VolumeMultiplier` — float

The entire Audio keyframe group is only serialized for HSP v3+. **[H2024]**

## 20.14 Environment keyframe — v7+

`FEnvironmentKeyFrame`:

1. `Time` — FTimespan
2. `BackgroundType` — enum/uint8
3. `BackgroundColor` — FLinearColor
4. `BackgroundName` — FString
5. `MaskType` — enum/uint8
6. `OpacityScale` — float
7. `ChromaKey1Color` — FLinearColor
8. `ChromaKey1Settings` — FLinearColor
9. `ChromaKey2Color` — FLinearColor
10. `ChromaKey2Settings` — FLinearColor
11. `ChromaKey3Color` — FLinearColor
12. `ChromaKey3Settings` — FLinearColor
13. `DespillColor` — FLinearColor
14. `DespillSettings` — FLinearColor
15. `LightColor` — FLinearColor
16. `AlphaCoordinates` — FLinearColor

The entire Environment keyframe group is only serialized for HSP v7+. **[H2024]**

Note that the 2024 enum only knows `None`, `ChromaKey`, and `AlphaPacked` mask types. The newer v0.12.0 **Alpha External** feature postdates this layout, so its HSP enum/serialization extension is not documented here. **[H2024, R0.12.0]**

## 20.15 External automation guidance for non-Image groups

The binary layout proves that these fields exist; it does **not** by itself prove a safe visual transfer function or sign convention for every camera/player combination. External tools should separate **measurement**, **recommendation**, and **permission to write**.

Practical conservative uses include:

- `Format.Zoom` / `Format.Pan`: remove persistent encoded borders after multi-frame confirmation. Avoid automatic crop for fisheye/cubemap sources unless the projection-specific geometry is understood.
- `Alignment.Position` / `Alignment.Rotation`: correct small, stable binocular mismatch. Do not treat ordinary horizontal disparity as an error; it carries stereo depth.
- `Stitch`: hold Shift/Scale/Shear/Flare/Slant neutral unless a native-export calibration or strong multi-frame seam model supports the change.
- `Orientation`: level a persistent small camera roll or correct an unambiguous rotation; artistic framing can make single-frame horizon guesses unsafe.
- `Lens`: known camera/lens profiles may be reproduced exactly, but an observed fisheye circle is not enough to invent proprietary calibration coefficients.
- `AutoFocus`: measured disparity can narrow a recommended focus range, but HereSphere's own autofocus remains authoritative.
- `Audio`: bounded `VolumeMultiplier` normalization is safer than timing changes. Validate `TimeOffset` sign with the installed build before automatic use.
- `Sync`: `StartingTicksOffset` and `TicksPerHourOffset` can represent initial offset and long-duration drift, but both require a trustworthy external timing estimate.

Manual user overrides should win over automatic suggestions, and unrelated groups should be preserved rather than regenerated casually. **[H2024, B2025, N2026]**

---

# 21. Advanced: HSP image settings

`FImageKeyFrame` is the HSP structure responsible for per-video image correction.

## 21.1 Serialization order

| Order | Field | Type | Condition |
|---:|---|---|---|
| 1 | `Time` | FTimespan | always |
| 2 | `Sharpen` | float | always |
| 3 | `ColorExposureBias` | float | always |
| 4 | `ColorWhiteTemp` | float | always |
| 5 | `ColorWhiteTint` | float | always |
| 6 | `ColorSaturation` | FVector4 | always |
| 7 | `ColorContrast` | FVector4 | always |
| 8 | `ColorGamma` | FVector4 | always |
| 9 | `ColorGain` | FVector4 | always |
| 10 | `ColorOffset` | FVector4 | always |
| 11 | `Shadows` | float | v4+ |
| 12 | `Midtones` | float | v4+ |
| 13 | `Highlights` | float | v4+ |

**[H2024]**

## 21.2 Neutral image values observed in the April 2025 PC build

Current saved-media/profile validation established the following neutral values:

| Field | Neutral value |
|---|---|
| `Sharpen` | `2.0` |
| `ColorExposureBias` | `0.0` |
| `ColorWhiteTemp` | `6500.0` |
| `ColorWhiteTint` | `0.0` |
| `ColorSaturation` | `(1, 1, 1, 1)` |
| `ColorContrast` | `(1, 1, 1, 1)` |
| `ColorGamma` | `(1, 1, 1, 1)` |
| `ColorGain` | `(1, 1, 1, 1)` |
| `ColorOffset` | `(0, 0, 0, 0)` |
| `Shadows` | `0.0` |
| `Midtones` | `1.0` |
| `Highlights` | `1.0` |

**[B2025]**

These are useful when constructing a neutral HSP profile. They should not be assumed to remain forever unchanged in future HSP versions.

## 21.3 Color-control semantics

HereSphere uses Unreal’s native color-grading pipeline for these controls. The relevant general behavior is:

- **Saturation**: blends luma/monochrome toward the working color; RGB components and the fourth/global component combine multiplicatively.
- **Contrast**: power-based contrast around an approximately 18% pivot in Unreal’s color-grading pipeline.
- **Gamma**: applied using the reciprocal gamma exponent.
- **Gain**: multiplicative color gain.
- **Offset**: additive color offset.
- **White temperature / tint**: feed Unreal’s white-balance/chromatic-adaptation stage.

The fourth component of the `FVector4` color controls acts as a global multiplier/adjustment combined with the per-channel XYZ values in Unreal’s grading shader.

### Native standalone-export confirmation

A native standalone HSP was exported from HereSphere after changing **only Contrast**. The resulting `FImageKeyFrame` contained: **[N2026]**

```text
ColorSaturation = (1.0, 1.0, 1.0, 1.0)
ColorContrast   = (1.0, 1.0, 1.0, 1.0750606)
ColorGamma      = (1.0, 1.0, 1.0, 1.0)
ColorGain       = (1.0, 1.0, 1.0, 1.0)
ColorOffset     = (0.0, 0.0, 0.0, 0.0)
```

This directly confirms that HereSphere stores the user-facing/global Contrast adjustment in the **fourth/global component**, leaving the first three per-channel components neutral. It supports the corresponding global forms for externally generated scalar controls:

```text
Saturation -> (1, 1, 1, S)
Contrast   -> (1, 1, 1, C)
Gamma      -> (1, 1, 1, G)
Offset     -> (0, 0, 0, B)
```

Per-channel white-balance correction can continue to use `ColorGain` RGB with a neutral fourth component, e.g. `(R, G, B, 1)`. **[N2026]**

## 21.4 Multiple Image keyframes and per-channel finishing

`FImageKeyFrames` is an array, so a profile may contain more than one complete Image record at different `FTimespan` values. This permits stable scene/lighting sections such as:

```text
00:00  grade A
11:42  grade B
27:15  grade C
```

An external analyzer should not generate continuous frame-by-frame white balance. A conservative implementation should require sustained before/after evidence, a minimum section duration, temporal hysteresis/non-maximum suppression, confidence gating, and a hard keyframe cap. The first active Image grade should begin at time zero so behavior is deterministic from playback start.

The RGB components of Saturation, Contrast, Gamma, and Offset are also usable independently from the fourth/global component. That makes brightness-dependent finishing possible—for example, a small green-shadow correction in RGB Offset while leaving global Offset unchanged. These channel values should remain small and evidence-backed because they are nonlinear color-grade controls, not a substitute for ordinary white balance. `ColorGain (R,G,B,1)` remains the clearest primary channel-balance operation. **[H2024, N2026]**

### `Shadows`, `Midtones`, `Highlights`

The 2024 HSP layout establishes their byte order and neutral values, but **does not document the exact HereSphere UI range or transfer function for these three floats**. Do not invent semantics beyond what can be validated against a specific player build. **[H2024, B2025]**

---

# 22. Advanced: HSP enums and serialization conventions

## 22.1 Enums from the 2024 HSP layout

### `EBackgroundType`

| Value | Name |
|---:|---|
| `0` | `GlobalSetting` |
| `1` | `Color` |
| `2` | `Passthrough` |

### `EMaskType` — HSP v7-era layout

| Value | Name |
|---:|---|
| `0` | `None` |
| `1` | `ChromaKey` |
| `2` | `AlphaPacked` |

### `EStereoMode`

| Value | Name |
|---:|---|
| `0` | `Mono` |
| `1` | `SideBySide` |
| `2` | `TopBottom` |
| `3` | `FullSideBySide` |

### `EProjectionMode`

| Value | Name |
|---:|---|
| `0` | `Perspective` |
| `1` | `Equirectangular` |
| `2` | `Fisheye` |
| `3` | `Equirectangular360` |
| `4` | `Cubemap` |
| `5` | `EquiangularCubemap` |

### `EAspectRatioType`

| Value | Name |
|---:|---|
| `0` | `Autodetect` |
| `1` | `Standard` |
| `2` | `HighDefinition` |
| `3` | `WideScreen` |
| `4` | `Custom` |

### `EOrientationType`

| Value | Name |
|---:|---|
| `0` | `Up` |
| `1` | `Left` |
| `2` | `Right` |
| `3` | `Down` |

**[H2024]**

## 22.2 Primitive serialization conventions

The 2024 layout uses Unreal `FArchive` serialization. In the inspected PC-era format:

- numeric values are little-endian on Windows
- `int32` = 4 bytes
- `int64` / `FTimespan` / `FDateTime` ticks = 8 bytes
- float = IEEE-754 32-bit
- reflected enums shown here serialize as byte-sized enum values in these structures
- persistent non-editor `FArchive` bools use the legacy 32-bit integer representation
- arrays are serialized as int32 count followed by elements
- strings use Unreal `FString` serialization; do not assume a fixed-length or always-UTF-8 representation

`FTimespan` uses Unreal ticks: **10,000,000 ticks per second** (100 ns per tick). API JSON time fields, by contrast, use **milliseconds**. Do not confuse the two domains.

## 22.3 Compression/container observation

The HSP/API docs describe HSPs as compressed binary files. Current saved-media data from the inspected PC build uses an Unreal `FArchiveSaveCompressedProxy`-compatible zlib envelope, including the Unreal package tag `0x9E2A83C1`, chunk tables and 128 KiB (`131072`) chunks; large saved-data streams can contain concatenated proxy segments. **[B2025]**

A native standalone HSP export showed that there are **two distinct layers** to keep separate: **[N2026]**

```text
Standalone .hsp file
└── Unreal FArchiveSaveCompressedProxy-compatible envelope
    └── int32 1024                  observed standalone outer prefix
        └── int32 7                 inner HSP / UMediaPreset version
            └── serialized v7 media-preset payload
```

The corresponding preset representation embedded in `Library.hsl` begins at the inner version instead:

```text
Library.hsl embedded media preset
└── int32 7                         HSP / UMediaPreset version
    └── serialized v7 media-preset payload
```

The native exported file was decompressed and then recompressed with the same Unreal-compatible compressor; when supplied the same raw bytes, the result reproduced the native export byte-for-byte. This verifies that the earlier incompatibility was the **missing decompressed standalone prefix**, not zlib/proxy compression or v7 field ordering. **[N2026]**

This is an **implementation observation**, not a promise that every future HSP/export container will forever use exactly the same envelope or prefix. If writing HSPs externally:

- retain version-awareness
- distinguish standalone-file wrapping from embedded media-preset representation
- self-parse what you generate
- test against the player version you target
- preserve unknown future wrappers/versions byte-for-byte instead of “upgrading” them with an older writer

---

# 23. Implementation recommendations

These are practical recommendations rather than mandatory JSON fields.

## 23.1 Use stable video-data URLs

Treat the video-data URL as a persistent scene identity. Avoid changing it merely because a file moved between storage roots if you want player-side state to survive.

## 23.2 Implement `/heresphere/scan`

Large libraries benefit enormously from bulk scan data. It avoids a GET for every scene whenever HereSphere rebuilds its search/sort metadata.

## 23.3 Respect `needsMediaSource`

Do not perform expensive signing, transcoding lookup, cloud API calls, or media-token generation when `needsMediaSource` is false.

## 23.4 Generate browse-friendly thumbnails

VR source frames often contain two eyes and lens distortion. Produce a single-eye/dewarped 2D thumbnail when possible rather than passing a raw SBS/fisheye frame directly.

## 23.5 Support byte ranges

Large VR files can be tens of gigabytes. Proper HTTP range serving is essential for responsive seeking.

## 23.6 Cache metadata, not authorization mistakes

Library/scan metadata is cache-friendly. Per-user favorite/rating/access state may not be. Use appropriate cache keys/headers when authentication changes the response.

## 23.7 Version or content-hash HSP URLs

If HSP content changes, a revisioned URL is safer than silently replacing bytes at a heavily cached URL.

Example:

```text
https://media.example.net/hsp/123-v7.hsp?rev=4f74c8d2
```

## 23.8 Preserve user write-backs

When `writeHSP`, `writeTags`, etc. are enabled, retain the user’s data transactionally. For HSPs, keep the raw original payload if the version cannot be parsed.

## 23.9 Do not expose unauthenticated deletion casually

If `deleteFile` is supported, require strong authorization and explicit product policy. A server may choose to never honor deletion even if it supports other write-backs.

## 23.10 Use HTTPS outside a trusted LAN

The original API design allows username/password JSON bodies and custom auth tokens. Plain HTTP is acceptable only in environments where traffic interception is not a concern. For Internet-facing deployments, use HTTPS.

---

# 24. API evolution timeline

This table focuses on public changes relevant to server developers.

| Release | Date | API-related change |
|---|---|---|
| v0.7 | 2022-06-27 | New HereSphere Web streaming API introduced. |
| v0.7.2 | 2022-09-10 | Server sync/write-back for favorites, ratings, tags, HSP and file deletion; `cameraIPD`; overwrite behavior. |
| v0.7.3 | 2022-10-05 | `needsMediaSource`; `/heresphere/scan`; multipart script handling. |
| v0.7.4 | 2023-01-06 | `eventServer`. |
| v0.8.0 | 2023-02-24 | External subtitle support. |
| v0.9.3 | 2023-05-20 | Event-server time-offset behavior improved. |
| v0.9.5 | 2023-08-18 | Auto Create/Update HSP player options; network stack/buffering improvements. |
| v0.10.0 | 2023-11-13 | `alphaPackedSettings`, `chromaKeySettings`, `hspArray`. |
| v0.10.1 | 2023-11-23 | Fixed Web API parsing of `chromaKeySettings` and `hspArray`. |
| v0.11.0 | 2024-06-25 | Event server buffering/suspended behavior fixed; timestamp-server fields expanded (separate feature). |
| v0.11.3 | 2024-09-02 | `handyTokenUrl`; player Full Side-by-Side mode / filename detection. |
| v0.11.4 | 2024-11-16 | Player option to always load HSP files for Web API videos. |
| v0.11.9 | 2025-06-14 | Duration sort in Web API/media library view. |
| v0.12.0 | 2026-01-16 | `alphaExternal`; external alpha passthrough; audio-track switching. |

See section 27 for official release-note URLs.

---

# 25. Known gaps and version-sensitive details

A good community reference should say what it **does not know**.

## 25.1 Full Side-by-Side Web API string

The player/HSP enum supports `FullSideBySide` and v0.11.3 added `_LRF`/`_SBSF` filename detection, but the supplied Web API doc only defines `mono`, `sbs`, and `tb`. The April 2025 Web API parser string table also visibly contains only `sbs` and `tb` in that field’s value table. The correct newer JSON token, if any, is not established here.

## 25.2 Event-server suspended/resumed numeric codes

Public release notes confirm suspended/resumed behavior can reach the event server, but the supplied API document defines only event IDs `0`–`3`. Additional numeric values are intentionally omitted from this reference until verified.

## 25.3 HSP versions after 7

The 2024 layout, inspected April 2025 saved data, and August 2026 native standalone export validate an **inner HSP/UMediaPreset version of 7**. The `1024` observed before that version in a standalone export is an outer standalone prefix, not evidence of “HSP version 1024.” This document does not claim future HSP layouts or standalone wrapper values are unchanged. **[H2024, B2025, N2026]**

## 25.4 `alphaExternal` HSP representation

The Web API `alphaExternal` URL is documented by v0.12.0 release notes. The corresponding newer HSP environment/mask enum layout was not present in the supplied 2024 v7 source layout and is therefore not reverse-specified here.

## 25.5 Shadows/midtones/highlights transfer function

The v7 HSP byte layout and neutral values are known; exact UI range/meaning is not documented in the supplied layout.

## 25.6 Platform release skew

Quest and Windows builds may not ship on identical dates or contain identical feature sets. A field documented in a newer Quest release may not be available in an older Windows binary, and vice versa. Servers targeting multiple client versions should degrade gracefully when optional fields are ignored.

---

# 26. Complete end-to-end examples

## 26.1 Root library

```json
{
  "access": 1,
  "library": [
    {
      "name": "Library",
      "list": [
        "https://media.example.net/heresphere/video/123"
      ]
    }
  ]
}
```

Response header:

```http
HereSphere-JSON-Version: 1
```

## 26.2 Bulk scan

```json
{
  "scanData": [
    {
      "link": "https://media.example.net/heresphere/video/123",
      "title": "Example VR Scene",
      "dateReleased": "2026-01-02",
      "dateAdded": "2026-01-03",
      "duration": 2400000.0,
      "rating": 4.5,
      "isFavorite": false,
      "tags": [
        {"name": "Studio:Example", "start": 0, "end": 0, "track": 2}
      ]
    }
  ]
}
```

## 26.3 Metadata-only video response

When `needsMediaSource` is false, media may be omitted:

```json
{
  "access": 1,
  "title": "Example VR Scene",
  "description": "",
  "thumbnailImage": "https://media.example.net/thumb/123.jpg",
  "dateReleased": "2026-01-02",
  "dateAdded": "2026-01-03",
  "duration": 2400000.0,
  "projection": "fisheye",
  "stereo": "sbs",
  "isEyeSwapped": false,
  "fov": 200.0,
  "lens": "MKX200",
  "cameraIPD": 6.5,
  "hspArray": [
    {"url": "https://media.example.net/hsp/123-v7.hsp", "version": 7}
  ],
  "eventServer": "https://media.example.net/events/123",
  "writeFavorite": true,
  "writeRating": true,
  "writeTags": true,
  "writeHSP": true
}
```

## 26.4 Playback response with media

```json
{
  "access": 1,
  "title": "Example VR Scene",
  "thumbnailImage": "https://media.example.net/thumb/123.jpg",
  "duration": 2400000.0,
  "projection": "fisheye",
  "stereo": "sbs",
  "fov": 200.0,
  "lens": "MKX200",
  "cameraIPD": 6.5,
  "hspArray": [
    {"url": "https://media.example.net/hsp/123-v7.hsp", "version": 7}
  ],
  "scripts": [
    {
      "name": "Main",
      "url": "https://media.example.net/script/123.funscript",
      "rating": 5.0
    }
  ],
  "subtitles": [
    {
      "name": "English",
      "language": "English",
      "url": "https://media.example.net/subtitle/123.en.srt"
    }
  ],
  "media": [
    {
      "name": "HEVC Original",
      "sources": [
        {
          "resolution": 2880,
          "height": 2880,
          "width": 5760,
          "size": 42000000000,
          "url": "https://media.example.net/media/123.mp4"
        }
      ]
    }
  ],
  "eventServer": "https://media.example.net/events/123",
  "writeFavorite": true,
  "writeRating": true,
  "writeTags": true,
  "writeHSP": true
}
```

## 26.5 HSP write-back

```json
{
  "isFavorite": true,
  "rating": 4.5,
  "hsp": "BASE64_ENCODED_HSP_BYTES"
}
```

POST this body to the same video-data URL in response to player edits when the corresponding write flags are enabled.

## 26.6 Playback event

```json
{
  "username": "ExampleUser",
  "id": "https://media.example.net/heresphere/video/123",
  "title": "Example VR Scene",
  "event": 1,
  "time": 12500.0,
  "speed": 1.0,
  "utc": 1760000000000.0,
  "connectionKey": "abcd1234"
}
```

---

# 27. Source notes

## Supplied developer material

- **D2023 — “HereSphere JSON API Version 1”**: developer document sourced from the HereSphere Discord API channel, supplied for this reference.
- **H2024 — HSP layout source**: `UMediaPreset` / keyframe serialization layout sourced from the HereSphere Discord channel, supplied for this reference.
- **B2025 — Windows build validation**: unencrypted Windows Shipping build timestamped 2025-04-30, used only to confirm parser field names/enums and current HSP-version behavior. No proprietary binaries or extracted assets are redistributed with this document.
- **N2026 — Native standalone HSP validation**: a user-exported standalone `.hsp` captured from HereSphere in August 2026 after a controlled one-setting Contrast change. It was used to verify the standalone `1024 + v7` decompressed structure, global color-vector component behavior, write-back container shape, and byte-level compatibility. The example file itself is not redistributed with this reference.

## Official public release notes

- v0.7.2 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/426164/update-v072-released
- v0.7.3 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/436152/update-v073-released
- v0.7.4 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/473049/update-v074-released
- v0.9.3 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/533176/update-v093-released
- v0.9.5 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/576641/update-v095-released
- v0.10.0 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/635013/update-v0100-released
- v0.10.1 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/639669/update-v0101-released
- v0.11.0 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/754843/update-v0110-released
- v0.11.3 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/792881/update-v0113-released
- v0.11.4 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/834620/update-v0114-released
- v0.11.9 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/966152/upload-v0119-released
- v0.12.0 — https://heresphere.itch.io/heresphere-vr-video-player-quest-2/devlog/1318258/update-v0120-released

## Contributing corrections

Because HereSphere continues to evolve and the most detailed API material has historically lived in Discord, corrections from the HereSphere developer/community are welcome. When updating this reference, prefer:

1. current official developer documentation,
2. public HereSphere release notes,
3. direct observation against a named player version,
4. clearly labeled inference only when unavoidable.

Please do not silently assign semantics to undocumented fields or enum values; versioned multimedia formats become much easier to maintain when “unknown” remains a valid answer.
