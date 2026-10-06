using Cove.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HereSphereExtension;

/// <summary>
/// Cove-facing settings API for the HereSphere settings page. Everything here is called from the
/// extension's own UI bundle (same-origin <c>extensionFetch</c>, Cove session auth), never by the
/// HereSphere client.
/// <list type="bullet">
/// <item><c>GET/PUT /api/heresphere/settings</c>: tag-category order, segment source, subtitles.
/// Extension-wide, so writing requires <c>extensions.configure</c>.</item>
/// <item><c>GET /api/heresphere/tokens</c> and <c>DELETE /api/heresphere/tokens/{id}</c>: list
/// and revoke the signed-in user's own API tokens. Cove's <c>RevokeApiTokenAsync</c> only revokes
/// tokens owned by the acting user, so one user can never revoke another's.</item>
/// </list>
/// </summary>
public sealed partial class HereSphereExtension
{
    private static readonly JsonSerializerOptions SettingsRequestJson = new() { PropertyNameCaseInsensitive = true };

    private async Task<IResult> GetSettingsEndpointAsync(HttpContext http, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        var principal = http.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Current;
        return Results.Json(new HereSphereSettingsResponse(
            settings.Settings,
            HereSphereTagCategories.DefaultOrder.ToDictionary(k => k, HereSphereTagCategories.Label),
            principal?.Has(Permissions.ExtensionsConfigure) == true,
            _store is not null));
    }

    private async Task<IResult> PutSettingsEndpointAsync(HttpContext http, CancellationToken ct)
    {
        var principal = http.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Current;
        if (principal?.Has(Permissions.ExtensionsConfigure) != true)
            return Results.Json(new { error = "Changing HereSphere settings requires the extensions.configure permission." },
                statusCode: StatusCodes.Status403Forbidden);

        HereSphereSettings? posted;
        try
        {
            posted = await JsonSerializer.DeserializeAsync<HereSphereSettings>(http.Request.Body, SettingsRequestJson, ct);
        }
        catch (JsonException)
        {
            posted = null;
        }
        if (posted is null)
            return Results.Json(new { error = "The request body must be a JSON settings object." }, statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var saved = await SaveSettingsAsync(posted, ct);
            return Results.Json(new HereSphereSettingsResponse(
                saved,
                HereSphereTagCategories.DefaultOrder.ToDictionary(k => k, HereSphereTagCategories.Label),
                true,
                true));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task<IResult> ListTokensEndpointAsync(HttpContext http, CancellationToken ct)
    {
        var principal = http.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Current;
        if (principal?.UserId is not int userId)
            return Results.Json(new { error = "Sign in to Cove to manage your tokens." }, statusCode: StatusCodes.Status401Unauthorized);

        var tokens = await http.RequestServices.GetRequiredService<ITokenService>().ListApiTokensAsync(userId, ct);
        return Results.Json(tokens.Select(t => new HereSphereTokenItem(
            t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.ExpiresAt,
            string.Equals(t.Name, HereSphereApiTokenName, StringComparison.Ordinal),
            principal.TokenId == t.Id)).ToList());
    }

    private async Task<IResult> RevokeTokenEndpointAsync(HttpContext http, CancellationToken ct)
    {
        var principal = http.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Current;
        if (principal?.UserId is not int userId)
            return Results.Json(new { error = "Sign in to Cove to manage your tokens." }, statusCode: StatusCodes.Status401Unauthorized);
        if (!Guid.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
            return Results.BadRequest();

        var tokens = http.RequestServices.GetRequiredService<ITokenService>();
        // Only the caller's own tokens; RevokeApiTokenAsync re-checks ownership itself.
        if (!(await tokens.ListApiTokensAsync(userId, ct)).Any(t => t.Id == id))
            return Results.NotFound();

        await tokens.RevokeApiTokenAsync(id, principal, ct);
        return Results.NoContent();
    }
}

public sealed record HereSphereSettingsResponse(
    [property: JsonPropertyName("settings")] HereSphereSettings Settings,
    [property: JsonPropertyName("categoryLabels")] IReadOnlyDictionary<string, string> CategoryLabels,
    [property: JsonPropertyName("canEdit")] bool CanEdit,
    [property: JsonPropertyName("storeAvailable")] bool StoreAvailable);

public sealed record HereSphereTokenItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("prefix")] string Prefix,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("lastUsedAt")] DateTime? LastUsedAt,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    [property: JsonPropertyName("isHereSphere")] bool IsHereSphere,
    [property: JsonPropertyName("isCurrent")] bool IsCurrent);
