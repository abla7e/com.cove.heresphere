> [!NOTE]
> I don't work in .NET, and I am not familiar with the cove codebase. This is created with Cline using qwen3.8 and Claude Opus 5.5, supervised by me to make sure it doesn't do anything too stupid. Design decisions are mine.

# HereSphere for Cove

Watch your [Cove](https://github.com/yourcove/cove) library in **HereSphere** — on a Quest, a
Windows VR headset, or the HereSphere desktop app — with full tag/performer/rating/favorite sync,
proper VR metadata, and no extra server to run.

This extension turns your Cove server into a HereSphere-compatible media source. Point HereSphere
at your Cove server's `/heresphere` URL, log in with your Cove account, and your whole library
shows up ready to browse and play — 2D or VR.

> [!NOTE]
> This page is for people who just want to **use** the extension. If you want to build it from
> source, understand its internals, or contribute changes, see
> [CONTRIBUTING.md](./CONTRIBUTING.md) instead.

## What you get

- **Your whole library, instantly.** Every video Cove knows about shows up in HereSphere's
  library view — no separate import or sync step. New videos appear next time HereSphere refreshes
  the catalog.
- **Log in with your existing Cove account.** No separate HereSphere password to manage. Logging
  in through the HereSphere app's username/password field authenticates against Cove directly and
  keeps you signed in on later launches.
- **Per-user ratings and favorites, both ways.** Rate a video or hit the heart icon in HereSphere
  and it's saved to *your* Cove account — the same rating/favorite you'd see in Cove's own web UI,
  and vice versa.
- **Tags, performers, and groups, organized and sorted.** Cove tags, performers, and your group
  memberships (including "Save for Later", "Watch History", and "Continue Watching") all show up
  as HereSphere's categorized tags, each in their own labeled section so the important ones sort to
  the top of the tag list instead of getting buried in one giant "Uncategorized" pile.
- **Resolution at a glance.** Instead of a cluttered list of exact pixel heights, videos are
  tagged with the resolution tier they actually deliver — `480p`, `720p`, `1080p`, `1440p`, and so
  on — including correct handling of portrait-oriented video.
- **Real VR metadata.** Projection (flat, equirectangular, fisheye, etc.), stereo layout
  (side-by-side / top-bottom), field of view, and lens calibration are all detected automatically
  from your video files and Cove's VR settings, so VR content plays back correctly without manual
  tagging.
- **Thumbnails and hover previews that match Cove.** The same cover art and preview clips you see
  in Cove's own UI show up in HereSphere.
- **Smooth seeking and streaming.** Videos stream with proper byte-range support, so scrubbing and
  seeking feel native — even on very large VR files.
- **Groups and collections as separate shelves.** Any static Cove group you've created (a
  curated collection, a watchlist, etc.) shows up as its own named section in HereSphere's
  library, in addition to showing up as a tag on each video it contains.
- **Private by default.** If you're not logged in, the extension reports an empty library —
  nothing in your collection is exposed to an anonymous HereSphere connection.

## Getting started

### 1. Install the extension on your Cove server

If you're a Cove admin, install this extension the same way you install any other Cove extension
(Cove's **Settings → Extensions** page, or by dropping the release `.zip` into your Cove
extensions folder and restarting/reloading extensions). Ask your Cove admin if you're not the one
running the server.

### 2. Point HereSphere at your server

In the HereSphere app, open the library/API settings and add a new Web API library with your
Cove server's address plus `/heresphere`, for example:

```
https://your-cove-server.example.com/heresphere
```

### 3. Log in

When prompted, enter your normal Cove username and password. HereSphere remembers the login for
next time — you shouldn't need to re-enter it unless you explicitly log out or your Cove account's
session is revoked.

That's it — your library should populate within a few seconds.

## Tag categories explained

Tags in HereSphere are grouped into categories so you can quickly tell what kind of information
you're looking at. With the default settings, the categories appear in this order:

| Category | Example | Meaning |
|---|---|---|
| `1. Tag` | `1. Tag:Shibari` | A tag you (or your scraper) applied in Cove. |
| `2. Group` | `2. Group:Watchlist` | A static group the video belongs to, or one of your personal "Save for Later" / "Watch History" / "Continue Watching" lists. |
| `3. Performer` | `3. Performer:Jane Doe` | A performer credited on the video in Cove. |
| `4. Studio` | `4. Studio:Example Studio` | The video's studio in Cove. |
| `5. Segment` | `5. Segment:Intro` | A Cove segment. These are timed, so HereSphere shows them as chapter markers on the player timeline. |
| `6. Resolution` | `6. Resolution:1080p` | The video's resolution tier (see below). |
| `7. Rating` | `7. Rating:4` | Your current star rating, also shown as a tag so you can search and filter by it. |
| `8. VR` | `8. VR:TRUE` | Whether Cove has this video flagged as VR content. |

HereSphere sorts tag categories by name, so the number in front decides the order. You can change
the order on **Settings → HereSphere** in Cove; the numbers follow whatever order you pick. A tag
with no `Category:` prefix at all would land in one big "Uncategorized" bucket, which is why every
tag has one.

### Segment markers

Segments you mark in Cove (time ranges with a title or a tag) show up as chapters above
HereSphere's seek bar, so you can jump straight to them in VR. By default only segments you created
by hand are shown; **Settings → HereSphere** can switch this to all segments (including AI or
automatic detections) or turn it off.

### Subtitles

If Cove found sidecar caption files next to a video (`.srt` or `.vtt`), HereSphere offers them as
subtitles, labeled by language. This can be turned off on **Settings → HereSphere**.

### Resolution tiers

Rather than tagging the video's exact pixel height (which produces dozens of near-duplicate tags
like `1082p`, `1078p`, `1074p` for files that are all "basically 1080p"), videos are bucketed into
the nearest standard tier they comfortably reach: `240p`, `360p`, `480p`, `540p`, `720p`, `900p`,
`1080p`, `1440p`, `1600p`, `2160p`, `2880p`, `3240p`, `3840p`, `4320p`, `5760p`, `7680p`, `8640p`.
A video only qualifies for a tier once it reaches at least 90% of that tier's pixel count — a
540p-tall video is never mislabeled `1080p` just because "it's sort of close". Portrait-oriented
video (taller than it is wide) is measured by width instead of height, so a 1080-wide portrait
clip is still correctly tagged `1080p` rather than being judged by its (much larger) height.

## Troubleshooting

- **My library is empty / shows "access 0".** You're not logged in, or your login failed. Re-enter
  your Cove username and password in HereSphere's library settings.
- **Ratings/favorites don't show up as stars/hearts.** Make sure you're logged in with
  username/password (not browsing anonymously), and check HereSphere's own settings for an
  "Overwrite Favorite"/"Overwrite Rating" toggle — HereSphere caches state locally per video and
  won't let server data overwrite that local cache unless the corresponding overwrite option is
  enabled.
- **A video's resolution tag looks wrong.** The tag reflects the *file's* actual resolution, not
  the resolution a title implies — a source file that's genuinely lower quality than its filename
  suggests will be tagged accordingly.
- **Settings changes don't show up.** HereSphere caches the library. Refresh it in HereSphere after
  saving settings in Cove.
- **Something else is wrong.** **Settings → HereSphere** in the Cove web UI also has a diagnostics
  section that shows exactly what the extension currently resolves for your account (access level,
  tokens, ratings, favorites). That's useful information to include in a bug report.

## Getting help

Please open an issue on this repository with as much detail as you can — what you expected to
happen, what happened instead, and (if relevant) a screenshot from the **Settings → HereSphere**
diagnostics page mentioned above.

