using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HereSphereExtension;

/// <summary>
/// Exposes the Cove library to HereSphere v1 clients. Registers the root-level HereSphere catalog
/// (per-video metadata plus inlined playable <c>sources</c>), a per-video data endpoint with
/// per-user rating/favorite write-back, media and thumbnail streaming, an auth-token exchange,
/// and a bulk scan — all backed by Cove's users, tokens, blobs, and media files. Unauthenticated
/// (guest or bad-login) requests receive an empty catalog.
/// </summary>
public sealed partial class HereSphereExtension : CoveExtensionBase, IApiExtension
{
    public override void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        // No extension-specific services are required. HereSphere resolves host services
        // (DbContext, ITokenService, IUserService, IBlobService) from the request scope at request time.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/heresphere", (HttpContext http, CancellationToken ct) => CatalogAsync(http, ct))
            .AllowCoveAnonymous();
        endpoints.MapPost("/heresphere", (HttpContext http, CancellationToken ct) => CatalogAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapPost("/heresphere/auth", (HttpContext http, CancellationToken ct) => AuthAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapGet("/heresphere/scan", (HttpContext http, CancellationToken ct) => ScanAsync(http, ct))
            .AllowCoveAnonymous();
        endpoints.MapPost("/heresphere/scan", (HttpContext http, CancellationToken ct) => ScanAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapGet("/heresphere/video/{id:int}", (HttpContext http, CancellationToken ct) => VideoAsync(http, ct))
            .AllowCoveAnonymous();
        endpoints.MapPost("/heresphere/video/{id:int}", (HttpContext http, CancellationToken ct) => VideoAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapGet("/heresphere/media/{id:int}", (HttpContext http, CancellationToken ct) => MediaAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapGet("/heresphere/thumbnail/{id:int}", (HttpContext http, CancellationToken ct) => ThumbnailAsync(http, ct))
            .AllowCoveAnonymous();

        endpoints.MapGet("/heresphere/preview/{id:int}", (HttpContext http, CancellationToken ct) => PreviewAsync(http, ct))
            .AllowCoveAnonymous();

        // {file} = "{captionId}.{srt|vtt}"; HereSphere picks its subtitle parser from the extension.
        endpoints.MapGet("/heresphere/subtitle/{file}", (HttpContext http, CancellationToken ct) => SubtitleAsync(http, ct))
            .AllowCoveAnonymous();

        // Cove-facing debug view of the current user's HereSphere integration state (settings tab at
        // /settings/heresphere). Requires an authenticated Cove principal (no specific permission); it is
        // read-only and served under the /api namespace so the extension's own frontend bundle can call it
        // through the same-origin extensionFetch helper.
        endpoints.MapGet("/api/heresphere/debug", (HttpContext http, CancellationToken ct) => DebugAsync(http, ct))
            .AllowWithoutCovePermission();

        // Settings page API (Cove session). Reading is open to any signed-in user; PUT checks
        // extensions.configure itself so it can return a readable error to the page.
        endpoints.MapGet("/api/heresphere/settings", (HttpContext http, CancellationToken ct) => GetSettingsEndpointAsync(http, ct))
            .AllowWithoutCovePermission();
        endpoints.MapPut("/api/heresphere/settings", (HttpContext http, CancellationToken ct) => PutSettingsEndpointAsync(http, ct))
            .AllowWithoutCovePermission();

        // The signed-in user's own API tokens (HereSphere's non-expiring tokens and any others).
        endpoints.MapGet("/api/heresphere/tokens", (HttpContext http, CancellationToken ct) => ListTokensEndpointAsync(http, ct))
            .AllowWithoutCovePermission();
        endpoints.MapDelete("/api/heresphere/tokens/{id:guid}", (HttpContext http, CancellationToken ct) => RevokeTokenEndpointAsync(http, ct))
            .AllowWithoutCovePermission();
    }

    /// <summary>
    /// HereSphere catalog: access level, an issued auth-token, the library list, and inlined media
    /// (one entry per video carrying metadata plus playable sources).
    /// </summary>
    private async Task<IResult> CatalogAsync(HttpContext http, CancellationToken ct)
    {
        var logger = http.RequestServices.GetRequiredService<ILogger<HereSphereExtension>>();
        var db = http.RequestServices.GetRequiredService<DbContext>();
        var config = http.RequestServices.GetRequiredService<CoveConfiguration>();
        var request = await ReadVideoRequestAsync(http, ct);
        TraceRequest(http, logger, request);
        var auth = await ResolveAccessAsync(http, request, http.RequestServices, ct);
        ApplyResolvedPrincipal(http, auth);
        TraceAuthResolution(http, logger, auth);
        var access = auth.Access;
        var token = auth.Token;
        var principal = auth.Principal;

        var response = new HereSphereCatalogResponse
        {
            Access = access,
            AuthToken = token,
            Banner = new HereSphereBanner { Link = BuildUrl(http, "/") },
        };

        // No resolved user (guest or bad login) → empty media and library; the HereSphere
        // client handles the empty state gracefully.
        if (principal is null || principal.UserId is null)
        {
            TraceResponse(http, logger, response);
            http.Response.Headers["HereSphere-JSON-Version"] = "1";
            return Results.Json(response);
        }

        var settings = await GetSettingsAsync(ct);
        var videos = await GetAllVideosAsync(db, ct);

        var snapshots = await http.RequestServices.GetRequiredService<IUserEngagementService>()
            .GetVideoSnapshotsAsync(videos.Select(v => v.Id), ct);
        var videoIds = videos.Select(v => v.Id).ToHashSet();
        var groupMap = await BuildVideoGroupMapAsync(db, videoIds, auth.Principal?.UserId, ct);
        var segmentMap = await BuildSegmentMarkerMapAsync(db, videos, settings, ct);

        response.Library =
        [
            new HereSphereLibrarySection
            {
                Name = "Library",
                List = videos.Select(v => BuildUrl(http, $"/heresphere/video/{v.Id}")).ToList(),
            },
        ];
        response.Library.AddRange(await BuildGroupSectionsAsync(db, http, videoIds, auth.Principal?.UserId, ct));
        response.Media = videos
            .Select(v => BuildMediaEntry(http, v, config, settings,
                snapshots.TryGetValue(v.Id, out var snapshot) ? snapshot : null,
                groupMap.GetValueOrDefault(v.Id),
                segmentMap.GetValueOrDefault(v.Id)))
            .ToList();

        TraceResponse(http, logger, response);
        http.Response.Headers["HereSphere-JSON-Version"] = "1";
        return Results.Json(response);
    }

    /// <summary>Exchange credentials (or resolve a header token) for a HereSphere auth-token.</summary>
    private async Task<IResult> AuthAsync(HttpContext http, CancellationToken ct)
    {
        var logger = http.RequestServices.GetRequiredService<ILogger<HereSphereExtension>>();
        var request = await ReadVideoRequestAsync(http, ct);
        TraceRequest(http, logger, request);
        var auth = await ResolveAccessAsync(http, request, http.RequestServices, ct);
        TraceAuthResolution(http, logger, auth);
        var access = auth.Access;
        var token = auth.Token;

        TraceAuthResponse(http, logger, access, token);
        http.Response.Headers["HereSphere-JSON-Version"] = "1";
        return Results.Json(new HereSphereAuthResponse { AuthToken = token, Access = access });
    }

    /// <summary>
    /// One library section per static Cove group that contains at least one video from the loaded
    /// catalog, plus one section per built-in per-user dynamic group (Save for Later, Watch
    /// History, Continue Watching) that has at least one matching video — every static group is
    /// included regardless of its "Show in video browsing" flag, since that flag only governs
    /// Cove's own browsing UI, not what a HereSphere client sees. Entries are ordered by the
    /// group items' <c>OrderIndex</c> for static groups, or by recency for dynamic groups.
    /// </summary>
    private static async Task<List<HereSphereLibrarySection>> BuildGroupSectionsAsync(
        DbContext db, HttpContext http, IReadOnlySet<int> videoIds, int? userId, CancellationToken ct)
    {
        var sections = new List<HereSphereLibrarySection>();

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
            var urls = group.GroupItems
                .Where(item => item.Kind is GroupItemKind.Video or GroupItemKind.VideoRange
                    && item.VideoId is not null
                    && videoIds.Contains(item.VideoId.Value))
                .OrderBy(item => item.OrderIndex)
                .Select(item => BuildUrl(http, $"/heresphere/video/{item.VideoId}"))
                .ToList();

            if (urls.Count > 0)
                sections.Add(new HereSphereLibrarySection { Name = group.Name, List = urls });
        }

        if (userId is int resolvedUserId)
        {
            var dynamicVideoIds = await BuildDynamicGroupVideoMapAsync(db, resolvedUserId, videoIds, ct);
            foreach (var (name, ids) in dynamicVideoIds)
            {
                if (ids.Count == 0)
                    continue;
                sections.Add(new HereSphereLibrarySection
                {
                    Name = name,
                    List = ids.Select(id => BuildUrl(http, $"/heresphere/video/{id}")).ToList(),
                });
            }
        }

        return sections;
    }
}