using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Destinatarios de los avisos de cumplimiento de la oficina (WISP): el responsable de seguridad, si
/// sigue activo en el tenant, más quienes administran la oficina (<c>settings.manage</c>). Sin repetidos;
/// el responsable va primero.
/// </summary>
internal static class OfficeComplianceRecipients
{
    /// <summary>
    /// Código de <c>PermissionCatalog.SettingsManage</c> (Auth, "Manage office settings"). BuildingBlocks no
    /// lo expone como constante; se refleja con el mismo código, igual que Tenant (OfficeSettingsPermissions).
    /// </summary>
    internal const string SettingsManagePermission = "settings.manage";

    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        Guid tenantId,
        Guid? securityCoordinatorUserId,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        IReadOnlyList<Guid> coordinator = [];
        if (securityCoordinatorUserId is { } coordinatorId && coordinatorId != Guid.Empty)
        {
            // Participants + validación vacía = solo "activo en el tenant" contra la proyección local.
            var resolved = await resolver.ResolveAsync(
                new Participants(tenantId, [coordinatorId]),
                RecipientValidation.None,
                ct
            );
            coordinator = resolved.IsSuccess ? resolved.Value : [];
        }

        var admins = await resolver.ResolveAsync(
            new ByPermission(tenantId, SettingsManagePermission),
            RecipientValidation.None,
            ct
        );

        return coordinator.Concat(admins.IsSuccess ? admins.Value : []).Distinct().ToArray();
    }
}
