using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace HereSphereExtension;

/// <summary>
/// Trace-level diagnostics for the HereSphere endpoints. Everything here exists to debug session/auth
/// resolution and rating/favorite write-back issues (e.g. a clicked catalog link resolving auth
/// differently than an address-bar navigation to the same video URL) and never runs in normal
/// operation: Cove's runtime log level is a temporary opt-in (Settings → System → "Start trace
/// logging", 15 minutes by default, or a persisted <c>Cove:LogLevel: Trace</c>), and every method below
/// starts with <see cref="ILogger.IsEnabled"/> so there is zero overhead — no header/cookie inspection,
/// no string formatting — when trace logging is off.
/// </summary>
public sealed partial class HereSphereExtension
{
    /// <summary>
    /// Logs the inbound request: which of the four credential signals Cove's auth resolution
    /// considers were present (the HereSphere <c>auth-token</c> header, a standard
    /// <c>Authorization</c> header, a <c>?access_token=</c> query token, or the
    /// <c>cove_access_token</c> browser cookie), plus the parsed body with the password redacted to a
    /// presence flag. This is the field to compare between a clicked-link request and an
    /// address-bar/manual request that otherwise hit the same URL.
    /// </summary>
    private static void TraceRequest(HttpContext http, ILogger logger, HereSphereVideoRequest? request)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        var authTokenHeader = GetAuthTokenHeader(http) is not null;
        var authorizationHeader = http.Request.Headers.TryGetValue("Authorization", out var authHeader)
            && !string.IsNullOrWhiteSpace(authHeader.ToString());
        var queryAccessToken = !string.IsNullOrWhiteSpace(http.Request.Query["access_token"].ToString());
        var accessCookie = http.Request.Cookies.TryGetValue("cove_access_token", out var cookie)
            && !string.IsNullOrWhiteSpace(cookie);

        logger.LogTrace(
            "HSP IN {Method} {Path}{Query} | auth-token={AuthTokenHeader} Authorization={AuthorizationHeader} " +
            "access_token(qs)={QueryAccessToken} cove_access_token(cookie)={AccessCookie} | " +
            "username={Username} hasPassword={HasPassword} needsMediaSource={NeedsMediaSource} " +
            "rating={Rating} isFavorite={IsFavorite} tagCount={TagCount}",
            http.Request.Method, http.Request.Path, http.Request.QueryString,
            authTokenHeader, authorizationHeader, queryAccessToken, accessCookie,
            request?.Username, request?.Password is not null, request?.NeedsMediaSource,
            request?.Rating, request?.IsFavorite, request?.Tags?.Count);
    }

    /// <summary>Logs how <see cref="ResolveAccessAsync"/> resolved the request: access level, which
    /// credential source won, and the resulting Cove principal (if any).</summary>
    private static void TraceAuthResolution(HttpContext http, ILogger logger, HereSphereAuthResolution auth)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        logger.LogTrace(
            "HSP AUTH {Method} {Path} access={Access} source={Source} userId={UserId} principalKind={PrincipalKind} tokenIssued={TokenIssued}",
            http.Request.Method, http.Request.Path, auth.Access, auth.Source,
            auth.Principal?.UserId, auth.Principal?.Kind, auth.Token is not null);
    }

    private static void TraceAuthResponse(HttpContext http, ILogger logger, int access, string? token)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        logger.LogTrace(
            "HSP OUT {Method} {Path} access={Access} tokenIssued={TokenIssued}",
            http.Request.Method, http.Request.Path, access, token is not null);
    }

    private static void TraceResponse(HttpContext http, ILogger logger, HereSphereCatalogResponse response)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        logger.LogTrace(
            "HSP OUT {Method} {Path} access={Access} librarySections={Sections} mediaCount={MediaCount} " +
            "ratedCount={RatedCount} favoriteCount={FavoriteCount}",
            http.Request.Method, http.Request.Path, response.Access, response.Library.Count, response.Media.Count,
            response.Media.Count(m => m.Rating is > 0), response.Media.Count(m => m.IsFavorite));
    }

    private static void TraceResponse(HttpContext http, ILogger logger, HereSphereScanResponse response)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        logger.LogTrace(
            "HSP OUT {Method} {Path} scanCount={ScanCount} ratedCount={RatedCount} favoriteCount={FavoriteCount}",
            http.Request.Method, http.Request.Path, response.ScanData.Count,
            response.ScanData.Count(e => e.Rating is > 0), response.ScanData.Count(e => e.IsFavorite));
    }

    private static void TraceResponse(HttpContext http, ILogger logger, int id, HereSphereVideoDataResponse response)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;

        logger.LogTrace(
            "HSP OUT {Method} {Path} id={Id} access={Access} rating={Rating} isFavorite={IsFavorite} " +
            "writeRating={WriteRating} writeFavorite={WriteFavorite} tagCount={TagCount}",
            http.Request.Method, http.Request.Path, id, response.Access, response.Rating, response.IsFavorite,
            response.WriteRating, response.WriteFavorite, response.Tags.Count);
    }

    /// <summary>Logs a rating/favorite write-back: exactly what the client posted and the resulting
    /// Cove-side engagement snapshot, so a write-back that silently does nothing is visible. No-ops
    /// when the request carried neither field (an ordinary playback POST never does).</summary>
    private static void TraceWriteBack(
        HttpContext http, ILogger logger, int id, HereSphereVideoRequest? request, UserEngagementSnapshot? snapshot)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
            return;
        if (request?.Rating is null && request?.IsFavorite is null)
            return;

        // The engagement service acts as the AMBIENT principal (ICurrentPrincipalAccessor.Current), not the
        // one HereSphere auth resolved — log it so a mismatch (e.g. ambient still Anonymous) is visible.
        // snapshotNull=True means Cove treated the video as nonexistent/invisible to that principal.
        var ambient = http.RequestServices.GetService<ICurrentPrincipalAccessor>()?.Current;
        logger.LogTrace(
            "HSP WRITE {Method} {Path} id={Id} postedRating={PostedRating} postedIsFavorite={PostedIsFavorite} " +
            "resultingCoveRating={ResultingCoveRating} resultingIsFavorite={ResultingIsFavorite} snapshotNull={SnapshotNull} " +
            "ambientUserId={AmbientUserId} ambientKind={AmbientKind}",
            http.Request.Method, http.Request.Path, id, request.Rating, request.IsFavorite,
            snapshot?.Rating, snapshot?.IsFavorite, snapshot is null,
            ambient?.UserId, ambient?.Kind);
    }
}
