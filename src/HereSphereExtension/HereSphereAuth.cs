using Cove.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HereSphereExtension;

public sealed partial class HereSphereExtension
{
    /// <summary>
    /// Name assigned to the durable Cove API token issued for HereSphere clients. Each user keeps
    /// exactly one live HereSphere token: a successful login revokes the previous one before
    /// issuing a new token, so the token the client last received is the only valid one.
    /// </summary>
    private const string HereSphereApiTokenName = "HereSphere";

    /// <summary>
    /// Reads the optional request body (credentials for login, and/or a rating/favorite write-back
    /// for video requests). A missing or malformed body simply means "no credentials" (guest).
    /// </summary>
    private static async Task<HereSphereVideoRequest?> ReadVideoRequestAsync(HttpContext http, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
            var json = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<HereSphereVideoRequest>(json);
        }
        catch
        {
            // A missing or malformed body simply means "no credentials" (guest access).
            return null;
        }
    }

    /// <summary>The value of the HereSphere <c>auth-token</c> header, or null when absent/blank.</summary>
    private static string? GetAuthTokenHeader(HttpContext http)
    {
        var value = http.Request.Headers.TryGetValue("auth-token", out var values)
            ? values.ToString().Trim()
            : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Determines HereSphere access, the auth-token to return, and the resolved Cove principal.
    /// An explicit <c>auth-token</c> header resolves the caller — HereSphere stores the token it
    /// was handed and resends it on later requests, so this is the steady-state path; a token that
    /// no longer resolves is a bad login (access -1). Otherwise the Cove session principal resolved
    /// by the request middleware is honored — it may come from a standard <c>Authorization</c>
    /// header, a <c>?access_token=</c> query token, or the <c>cove_access_token</c> browser cookie,
    /// so a logged-in browser (cookie only) is treated exactly like a valid HSP auth-token; an
    /// unresolvable/absent session degrades to guest rather than bad login. Valid username/password
    /// in the body verifies against Cove, records the login, then issues a fresh durable unscoped,
    /// non-expiring <c>cove_pat_*</c> API token (revoking the previous HereSphere token) and returns
    /// it raw (no <c>Bearer</c> prefix). No credentials at all means guest access (0).
    /// </summary>
    private static async Task<HereSphereAuthResolution> ResolveAccessAsync(
        HttpContext http, HereSphereVideoRequest? request, IServiceProvider services, CancellationToken ct)
    {
        var tokenService = services.GetRequiredService<ITokenService>();
        var userService = services.GetRequiredService<IUserService>();
        var principalAccessor = services.GetRequiredService<ICurrentPrincipalAccessor>();
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var userAgent = http.Request.Headers.UserAgent.ToString();

        // 1) Explicit HereSphere auth-token.
        var headerToken = GetAuthTokenHeader(http);
        if (headerToken is not null)
        {
            var principal = await tokenService.ResolveAsync(ToBearer(headerToken), ip, userAgent, ct);
            if (principal is null)
                return BadLogin();
            principalAccessor.Set(principal);
            return new HereSphereAuthResolution(1, headerToken, principal, HereSphereAuthSource.AuthorizationHeader);
        }

        // 2) Cove session principal. The request middleware (CurrentPrincipalMiddleware) has ALREADY
        //    resolved the caller from a standard Authorization header, a ?access_token= query token,
        //    or the cove_access_token browser cookie into ICurrentPrincipalAccessor.Current before
        //    this endpoint ran. A logged-in Cove user therefore drives HSP access: a normal browser
        //    GET /heresphere (carrying only the cookie, no Authorization header) is treated exactly
        //    like a valid HSP auth-token. Absent/anonymous session degrades to guest, not bad login.
        var current = principalAccessor.Current;
        if (current is not null && current.UserId is not null)
        {
            principalAccessor.Set(current);
            return new HereSphereAuthResolution(1, null, current, HereSphereAuthSource.CoveSession);
        }

        // 3) username/password in the body: verify, then issue (re-issue) the durable HereSphere API token.
        var username = request?.Username;
        var password = request?.Password;
        if (username is not null
            && password is not null
            && username.Trim().Length > 0
            && password.Length > 0)
        {
            var user = await userService.FindByUsernameAsync(username.Trim(), ct);
            if (user is null || !user.IsActive || user.IsLocked || !user.HasPassword)
                return BadLogin();
            if (!await userService.VerifyPasswordAsync(user.Id, password, ct))
            {
                await userService.RecordLoginFailureAsync(user.Id, ct);
                return BadLogin();
            }
            await userService.RecordLoginSuccessAsync(user.Id, ip, ct);

            var principal = await services.GetRequiredService<IExistingUserPrincipalResolver>()
                .ResolveExistingUserAsync(user.Id, ip, userAgent, ct);
            if (principal is null)
                return BadLogin();

            // Keep exactly one live HereSphere token per user: revoke the previous one(s), then
            // issue a fresh unscoped, non-expiring token that resolves on every later request.
            foreach (var existing in await tokenService.ListApiTokensAsync(user.Id, ct))
            {
                if (string.Equals(existing.Name, HereSphereApiTokenName, StringComparison.Ordinal))
                    await tokenService.RevokeApiTokenAsync(existing.Id, principal, ct);
            }

            var issued = await tokenService.CreateApiTokenAsync(
                user.Id, HereSphereApiTokenName, scope: null, expiresAt: null, principal, ct);
            principalAccessor.Set(principal);
            return new HereSphereAuthResolution(1, issued.PlaintextToken, principal, HereSphereAuthSource.Credentials);
        }

        // 4) No credentials at all → guest access.
        return Guest();
    }

    /// <summary>
    /// Makes the principal resolved by <see cref="ResolveAccessAsync"/> the ambient Cove principal
    /// for the rest of the calling endpoint. Call it directly from the endpoint, right after
    /// <c>var auth = await ResolveAccessAsync(...)</c>.
    /// <para>
    /// <b>This method must stay synchronous (non-<c>async</c>).</b> <see cref="ICurrentPrincipalAccessor"/>
    /// is backed by an <see cref="AsyncLocal{T}"/>, and a value written inside an <c>async</c> method
    /// is discarded when that method's task completes — it never flows back to the awaiting caller.
    /// That is why the <c>principalAccessor.Set(...)</c> calls inside <see cref="ResolveAccessAsync"/>
    /// have no effect on the endpoint, and why 0.5.1's <c>async</c> wrapper around it did not fix
    /// anything either. A plain synchronous method writes into the caller's own execution context,
    /// so the value is visible to everything the endpoint awaits afterward.
    /// </para>
    /// <para>
    /// Without this, <see cref="IUserEngagementService"/> (which reads
    /// <c>ICurrentPrincipalAccessor.Current</c>, not a parameter) runs as the anonymous principal
    /// Cove's <c>CurrentPrincipalMiddleware</c> set for an <c>auth-token</c>-header request. Cove's
    /// video authorization filter then hides every video, so the engagement service treats the video
    /// as nonexistent: reads return no rating/favorite and writes are dropped
    /// (<c>resultingCoveRating=null resultingIsFavorite=null</c> in the trace log).
    /// </para>
    /// </summary>
    private static void ApplyResolvedPrincipal(HttpContext http, HereSphereAuthResolution auth)
    {
        if (auth.Principal is not null)
            http.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Set(auth.Principal);
    }

    private static string ToBearer(string value)
    {
        value = value.Trim();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value
            : "Bearer " + value;
    }

    private static HereSphereAuthResolution Guest() => new(0, null, null, HereSphereAuthSource.Guest);

    private static HereSphereAuthResolution BadLogin() => new(-1, null, null, HereSphereAuthSource.BadLogin);
}

/// <summary>
/// The credential source that determined a <see cref="HereSphereAuthResolution"/>, reported so the debug
/// view can show exactly which path <c>GET /heresphere</c> (and the scan/video endpoints) took.
/// </summary>
public enum HereSphereAuthSource
{
    /// <summary>No credentials and no Cove session user (anonymous guest).</summary>
    Guest,
    /// <summary>An explicit, valid <c>auth-token</c> header (the steady-state HereSphere client path).</summary>
    AuthorizationHeader,
    /// <summary>The Cove session principal resolved by the request middleware — an <c>Authorization</c>
    /// header credential, a <c>?access_token=</c> query token, or the <c>cove_access_token</c> browser cookie.</summary>
    CoveSession,
    /// <summary>Username/password submitted in the request body (verified, then a durable token is issued).</summary>
    Credentials,
    /// <summary>Presented credentials that could not be resolved (an invalid <c>auth-token</c> or a failed login).</summary>
    BadLogin,
}

/// <summary>The result of resolving HereSphere access for a request.</summary>
public sealed record HereSphereAuthResolution(
    int Access,
    string? Token,
    CovePrincipal? Principal,
    HereSphereAuthSource Source);