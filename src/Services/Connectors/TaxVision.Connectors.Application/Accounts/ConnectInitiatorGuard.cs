using BuildingBlocks.Permissions;
using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Falla cerrado si quien conecta un buzón ya no está activo en la oficina (retirado o suspendido).
/// Cubre el intento OAuth que vive en Redis ~10 min y se completa después de que corrió el retiro.
/// Lee la proyección local de permisos con el mismo puerto que la autorización normal del servicio
/// (<c>ProjectionPermissionsSource</c>): Connectors no registra recuperación pull desde Auth, así que
/// sin fila (o inactiva) se rechaza, igual que el <c>[HasPermission]</c> que ya exigió el inicio.
/// PlatformAdmin no tiene proyección en el tenant (también se la salta la autorización normal).
/// </summary>
public static class ConnectInitiatorGuard
{
    public const string InactiveCode = "Connectors.Initiator.Inactive";

    public static readonly Error Inactive = new(
        InactiveCode,
        "Your account is no longer active in this office, so the mailbox was not connected."
    );

    public static async Task<Result> EnsureActiveAsync(
        Guid tenantId,
        Guid userId,
        bool initiatorIsPlatformAdmin,
        IUserPermissionsProjectionReader projections,
        CancellationToken ct
    )
    {
        if (initiatorIsPlatformAdmin)
            return Result.Success();

        var snapshot = await projections.GetSnapshotAsync(tenantId, userId, ct);
        return snapshot is null ? Result.Failure(Inactive) : Result.Success();
    }
}
