using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.Growth.Api.Common;

public static class ClaimsPrincipalExtensions
{
    private const string MappedAudienceClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/audience";

    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var value =
            principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out userId);
    }

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId) =>
        Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out tenantId);

    /// <summary>
    /// Delega en BuildingBlocks para que el criterio de PlatformAdmin (claim <c>actor_type</c>, no
    /// el nombre del rol) viva en un solo lugar. Se llama con el nombre completo porque esta clase
    /// se llama igual que la de BuildingBlocks y un <c>using</c> haría ambiguas sus otras firmas.
    /// </summary>
    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) =>
        BuildingBlocks.ActorTypeAuthorization.ClaimsPrincipalExtensions.IsPlatformAdmin(principal);

    public static bool HasScope(this ClaimsPrincipal principal, string requiredScope) =>
        principal
            .FindAll(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

    public static bool HasAudience(this ClaimsPrincipal principal, string requiredAudience) =>
        principal
            .FindAll(claim => claim.Type is JwtRegisteredClaimNames.Aud or MappedAudienceClaim)
            .Any(claim => string.Equals(claim.Value, requiredAudience, StringComparison.Ordinal));
}
