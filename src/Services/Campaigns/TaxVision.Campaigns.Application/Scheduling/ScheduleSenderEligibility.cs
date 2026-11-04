using BuildingBlocks.Authorization;
using TaxVision.Campaigns.Application.Permissions.Abstractions;

namespace TaxVision.Campaigns.Application.Scheduling;

/// <summary>
/// Quién puede disparar un agendado en su nombre: activo en este tenant y con <c>campaigns.send</c>, según
/// la proyección local de permisos. Sin proyección → no (fail-closed). Lo usan el traspaso por retiro y la
/// reanudación.
/// </summary>
public static class ScheduleSenderEligibility
{
    public static async Task<EligibleScheduleSender?> ResolveAsync(
        IUserPermissionsProjectionRepository permissions,
        Guid tenantId,
        Guid userId,
        CancellationToken ct
    )
    {
        if (userId == Guid.Empty)
            return null;

        var projection = await permissions.GetAsync(tenantId, userId, ct);
        if (projection is not { IsActive: true })
            return null;

        var codes = projection.PermissionCodes();
        return codes.Contains(CampaignsPermissions.Send, StringComparer.Ordinal)
            ? new EligibleScheduleSender(userId, codes.Contains(CustomersPermissions.ViewAll, StringComparer.Ordinal))
            : null;
    }
}

/// <summary>Usuario que puede disparar agendados y si ve a todos los clientes de la oficina.</summary>
public sealed record EligibleScheduleSender(Guid UserId, bool CanViewAllCustomers);
