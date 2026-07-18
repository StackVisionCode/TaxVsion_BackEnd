using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Api.Common;

/// <summary>
/// Extrae tenant/actor/scope del JWT — el mismo trio de claims que FilesController,
/// FoldersController y RecycleBinController necesitan en cada accion. Un solo lugar
/// evita que la logica de parseo (y un futuro cambio de claim) se desincronice entre
/// controllers copiados.
/// </summary>
internal static class StorageIdentity
{
    public static bool TryGet(
        this ClaimsPrincipal user,
        out Guid tenantId,
        out Guid actorId,
        out StorageActorScope scope
    )
    {
        actorId = Guid.Empty;
        scope = new StorageActorScope(false, null);
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out tenantId))
            return false;

        var subject =
            user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(subject, out actorId))
            return false;

        var isCustomerPortal = string.Equals(
            user.FindFirst("actor_type")?.Value,
            "CustomerPortal",
            StringComparison.OrdinalIgnoreCase
        );
        Guid? customerId = Guid.TryParse(user.FindFirst("customer_id")?.Value, out var parsedCustomerId)
            ? parsedCustomerId
            : null;
        scope = new StorageActorScope(isCustomerPortal, customerId);
        return true;
    }
}
