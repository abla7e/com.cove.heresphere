using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Sdk;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;

namespace HereSphereExtension;

public sealed partial class HereSphereExtension
{
    /// <summary>
    /// Cove-facing debug view of the current user's HereSphere integration state: Cove identity/session,
    /// HereSphere access (the durable API token issued for the user), per-user ratings and favorites, and
    /// HSP write/persistence metadata. This endpoint is strictly read-only: it never mutates Cove or
    /// HereSphere state. It requires an authenticated Cove principal (no extra permission) and uses the
    /// Cove session rather than the HereSphere username/password auth path.
    /// </summary>
    private async Task<IResult> DebugAsync(HttpContext http, CancellationToken ct)
    {
        var services = http.RequestServices;
        var principalAccessor = services.GetRequiredService<ICurrentPrincipalAccessor>();
        var principal = principalAccessor.Current;

        // Defensive: an authenticated principal is required, but some principal kinds (system, share link)
        // have no associated user and therefore no per-user HereSphere state to inspect.
        if (principal is null || principal.UserId is null)
        {
            var message = principal is null
                ? "No Cove principal is available for this request."
                : $"Cove principal kind '{principal.Kind}' has no associated user; per-user HereSphere state is not available.";
            return Results.Json(new
            {
                error = message,
                identity = principal is null ? (object?)null : DescribePrincipal(principal),
            },
            statusCode: principal is null ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden);
        }

        var db = services.GetRequiredService<DbContext>();
        var userId = principal.UserId.Value;

        // Live HSP auth resolution: run the EXACT same code path the /heresphere catalog, scan and video
        // endpoints use (Cove auth drives HSP access), so the debug view reports the real resolved access
        // level and credential source — not merely whether a durable token exists. A logged-in browser's
        // cove_access_token cookie is a valid session credential, exactly like a valid HSP auth-token.
        var debugRequest = await ReadVideoRequestAsync(http, ct);
        var authResolution = await ResolveAccessAsync(http, debugRequest, services, ct);
        // ResolveAccessAsync may re-point the accessor; restore this request's principal for any host
        // logic that runs after the endpoint.
        principalAccessor.Set(principal);
        var authTokenHeaderPresent = GetAuthTokenHeader(http) is not null;
        var standardAuthorizationHeaderPresent =
            http.Request.Headers.TryGetValue("Authorization", out var standardAuth)
            && !string.IsNullOrWhiteSpace(standardAuth.ToString());
        var queryAccessTokenPresent =
            !string.IsNullOrWhiteSpace(http.Request.Query["access_token"].ToString());
        var coveAccessTokenCookiePresent =
            http.Request.Cookies.TryGetValue("cove_access_token", out var coveCookieValue)
            && !string.IsNullOrWhiteSpace(coveCookieValue);

        // HereSphere access state: the durable HereSphere API token(s) issued for this user (each user keeps
        // exactly one live "HereSphere" token; listing it proves the user is connected to HereSphere).
        var tokenService = services.GetRequiredService<ITokenService>();
        var allTokens = (await tokenService.ListApiTokensAsync(userId, ct)).ToList();
        var hereSphereTokens = allTokens
            .Where(t => string.Equals(t.Name, HereSphereApiTokenName, StringComparison.Ordinal)).ToList();
        var otherTokens = allTokens
            .Where(t => !string.Equals(t.Name, HereSphereApiTokenName, StringComparison.Ordinal)).ToList();

        // Per-user ratings (all host types; HereSphere writes Video ratings on the "overall" aspect).
        var ratings = await db.Set<Rating>().AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.HostType).ThenBy(r => r.HostId).ThenBy(r => r.Aspect)
            .ToListAsync(ct);

        // Per-user favorites (all host types; HereSphere writes Video favorites).
        var favorites = await db.Set<UserEntityAffinity>().AsNoTracking()
            .Where(a => a.UserId == userId && a.IsFavorite)
            .OrderBy(a => a.HostType).ThenBy(a => a.HostId)
            .ToListAsync(ct);

        // Human-readable titles for video ratings/favorites (bypass Cove content/visibility filters so the
        // debug view shows titles even for videos that are not otherwise visible to this user).
        var videoIds = ratings
            .Where(r => r.HostType == RatingHostType.Video).Select(r => r.HostId)
            .Concat(favorites.Where(f => f.HostType == AffinityHostType.Video).Select(f => f.HostId))
            .ToHashSet();
        var videoTitles = videoIds.Count > 0
            ? (await db.Set<Video>().IgnoreQueryFilters().AsNoTracking()
                .Where(v => videoIds.Contains(v.Id)).ToListAsync(ct))
                .ToDictionary(v => v.Id, v => ResolveTitle(v))
            : new Dictionary<int, string>();

        var engagementServiceAvailable = services.GetService<IUserEngagementService>() is not null;

        var response = new HereSphereDebugResponse(
            Identity: DescribePrincipal(principal),
            Access: new HereSphereDebugAccess(
                HasHereSphereToken: hereSphereTokens.Count > 0,
                HereSphereTokens: hereSphereTokens.Select(DescribeToken).ToList(),
                OtherApiTokens: otherTokens.Select(DescribeToken).ToList()),
            Resolution: new HereSphereDebugResolution(
                Access: authResolution.Access,
                Source: authResolution.Source.ToString(),
                ResolvedPrincipalUserId: authResolution.Principal?.UserId,
                ResolvedPrincipalKind: authResolution.Principal?.Kind.ToString(),
                ResolvedTokenPresent: authResolution.Token is not null,
                AuthTokenHeaderPresent: authTokenHeaderPresent,
                StandardAuthorizationHeaderPresent: standardAuthorizationHeaderPresent,
                QueryAccessTokenPresent: queryAccessTokenPresent,
                CoveAccessTokenCookiePresent: coveAccessTokenCookiePresent),
            Ratings: ratings.Select(r => new HereSphereDebugRating(
                Id: r.Id,
                HostType: r.HostType.ToString(),
                HostId: r.HostId,
                Title: r.HostType == RatingHostType.Video && videoTitles.TryGetValue(r.HostId, out var ratingTitle) ? ratingTitle : null,
                Aspect: r.Aspect,
                CoveValue: r.Value,
                HspValue: ToHspRating(r.Value))).ToList(),
            Favorites: favorites.Select(f => new HereSphereDebugFavorite(
                Id: f.Id,
                HostType: f.HostType.ToString(),
                HostId: f.HostId,
                Title: f.HostType == AffinityHostType.Video && videoTitles.TryGetValue(f.HostId, out var favoriteTitle) ? favoriteTitle : null,
                FavoritedAt: f.FavoritedAt,
                ViewCount: f.ViewCount,
                CompleteCount: f.CompleteCount,
                TotalConsumedSec: f.TotalConsumedSec,
                LastConsumedAt: f.LastConsumedAt)).ToList(),
            Persistence: new HereSphereDebugPersistence(
                CanWriteEngagement: principal.UserId is not null,
                EngagementServiceAvailable: engagementServiceAvailable,
                EngagementServiceType: typeof(IUserEngagementService).AssemblyQualifiedName ?? typeof(IUserEngagementService).FullName ?? "IUserEngagementService",
                RatingCount: ratings.Count,
                FavoriteCount: favorites.Count,
                RatingScale: "cove 0-100; hsp 0-5; hsp = cove / 20, cove = round(hsp * 20) clamped to [0,100]",
                FavoriteField: "UserEntityAffinity.IsFavorite + FavoritedAt",
                RatingHostTypeValues: Enum.GetNames(typeof(RatingHostType)),
                AffinityHostTypeValues: Enum.GetNames(typeof(AffinityHostType))));

        return Results.Json(response);
    }

    private static HereSphereDebugIdentity DescribePrincipal(CovePrincipal principal) => new(
        principal.UserId,
        principal.Username,
        principal.Kind.ToString(),
        principal.Roles.ToArray(),
        principal.TokenId,
        principal.Ip,
        principal.UserAgent);

    private static HereSphereDebugToken DescribeToken(ApiTokenDto token) => new(
        token.Id,
        token.Name,
        token.Prefix,
        token.Scope?.ToList(),
        token.CreatedAt,
        token.LastUsedAt,
        token.ExpiresAt);

    /// <summary>
    /// Contributes the HereSphere settings tab (rendered at /settings/heresphere) and the debug panel that
    /// renders the <c>HereSphereDebug</c> component from this extension's JS bundle.
    /// </summary>
    public override UIManifest GetUIManifest()
    {
        var builder = ManifestBuilder();
        builder.AddSettingsTab(
            key: "heresphere",
            label: "HereSphere",
            layout: SettingsTabLayout.Page,
            order: 100,
            icon: "plug",
            description: "HereSphere settings (tag category order, segment markers, subtitles), your API tokens, and integration diagnostics.");
        builder.AddSettingsSection(
            targetTab: "heresphere",
            label: "HereSphere Settings",
            componentName: "HereSphereSettings",
            id: $"{Id}:heresphere:settings",
            order: 10);
        builder.AddSettingsSection(
            targetTab: "heresphere",
            label: "HereSphere Debug",
            componentName: "HereSphereDebug",
            id: $"{Id}:heresphere",
            order: 100);
        return builder.Build();
    }
}


/// <summary>Response for <c>GET /api/heresphere/debug</c>.</summary>
public sealed record HereSphereDebugResponse(
    [property: JsonPropertyName("identity")] HereSphereDebugIdentity Identity,
    [property: JsonPropertyName("access")] HereSphereDebugAccess Access,
    [property: JsonPropertyName("resolution")] HereSphereDebugResolution Resolution,
    [property: JsonPropertyName("ratings")] IReadOnlyList<HereSphereDebugRating> Ratings,
    [property: JsonPropertyName("favorites")] IReadOnlyList<HereSphereDebugFavorite> Favorites,
    [property: JsonPropertyName("persistence")] HereSphereDebugPersistence Persistence);

/// <summary>Current Cove identity/session for the authenticated request.</summary>
public sealed record HereSphereDebugIdentity(
    [property: JsonPropertyName("userId")] int? UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("tokenId")] Guid? TokenId,
    [property: JsonPropertyName("ip")] string? Ip,
    [property: JsonPropertyName("userAgent")] string? UserAgent);

/// <summary>HereSphere access state derived from the user's durable API token(s).</summary>
public sealed record HereSphereDebugAccess(
    [property: JsonPropertyName("hasHereSphereToken")] bool HasHereSphereToken,
    [property: JsonPropertyName("hereSphereTokens")] IReadOnlyList<HereSphereDebugToken> HereSphereTokens,
    [property: JsonPropertyName("otherApiTokens")] IReadOnlyList<HereSphereDebugToken> OtherApiTokens);

/// <summary>
/// Live HSP auth resolution for this request — what <c>GET /heresphere</c> (and the scan/video endpoints)
/// actually resolve via the same <c>ResolveAccessAsync</c> path Cove auth drives. Distinguishes the Cove
/// session (cookie) credential from a header auth-token or username/password, and reports which inputs
/// were present, so "durable token present" is no longer the only access signal.
/// </summary>
public sealed record HereSphereDebugResolution(
    [property: JsonPropertyName("access")] int Access,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("resolvedPrincipalUserId")] int? ResolvedPrincipalUserId,
    [property: JsonPropertyName("resolvedPrincipalKind")] string? ResolvedPrincipalKind,
    [property: JsonPropertyName("resolvedTokenPresent")] bool ResolvedTokenPresent,
    [property: JsonPropertyName("authTokenHeaderPresent")] bool AuthTokenHeaderPresent,
    [property: JsonPropertyName("standardAuthorizationHeaderPresent")] bool StandardAuthorizationHeaderPresent,
    [property: JsonPropertyName("queryAccessTokenPresent")] bool QueryAccessTokenPresent,
    [property: JsonPropertyName("coveAccessTokenCookiePresent")] bool CoveAccessTokenCookiePresent);

/// <summary>A Cove API token (a user's HereSphere token is the one named "HereSphere").</summary>
public sealed record HereSphereDebugToken(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("prefix")] string Prefix,
    [property: JsonPropertyName("scope")] IReadOnlyList<string>? Scope,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("lastUsedAt")] DateTime? LastUsedAt,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt);

/// <summary>One rating row for the current user, with both Cove and HSP scales.</summary>
public sealed record HereSphereDebugRating(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("hostType")] string HostType,
    [property: JsonPropertyName("hostId")] int HostId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("aspect")] string Aspect,
    [property: JsonPropertyName("coveValue")] int CoveValue,
    [property: JsonPropertyName("hspValue")] double? HspValue);

/// <summary>One favorite (UserEntityAffinity row) for the current user.</summary>
public sealed record HereSphereDebugFavorite(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("hostType")] string HostType,
    [property: JsonPropertyName("hostId")] int HostId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("favoritedAt")] DateTime? FavoritedAt,
    [property: JsonPropertyName("viewCount")] int ViewCount,
    [property: JsonPropertyName("completeCount")] int CompleteCount,
    [property: JsonPropertyName("totalConsumedSec")] double TotalConsumedSec,
    [property: JsonPropertyName("lastConsumedAt")] DateTime? LastConsumedAt);

/// <summary>HSP write/persistence metadata and engagement counts for the current user.</summary>
public sealed record HereSphereDebugPersistence(
    [property: JsonPropertyName("canWriteEngagement")] bool CanWriteEngagement,
    [property: JsonPropertyName("engagementServiceAvailable")] bool EngagementServiceAvailable,
    [property: JsonPropertyName("engagementServiceType")] string EngagementServiceType,
    [property: JsonPropertyName("ratingCount")] int RatingCount,
    [property: JsonPropertyName("favoriteCount")] int FavoriteCount,
    [property: JsonPropertyName("ratingScale")] string RatingScale,
    [property: JsonPropertyName("favoriteField")] string FavoriteField,
    [property: JsonPropertyName("ratingHostTypeValues")] IReadOnlyList<string> RatingHostTypeValues,
    [property: JsonPropertyName("affinityHostTypeValues")] IReadOnlyList<string> AffinityHostTypeValues);

