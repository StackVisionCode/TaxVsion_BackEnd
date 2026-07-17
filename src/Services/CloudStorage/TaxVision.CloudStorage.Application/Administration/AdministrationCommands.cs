using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Administration;

/// <summary>
/// Fase C3 — habilita/deshabilita links de compartir con Visibility.Public para el
/// tenant. Deshabilitado por defecto (datos fiscales); solo un actor con
/// cloudstorage.settings.manage puede cambiarlo (ver StorageAdministrationController).
/// </summary>
public sealed record SetPublicSharingPolicyCommand(Guid TenantId, bool Allow);

public static class SetPublicSharingPolicyHandler
{
    public static async Task<Result> Handle(
        SetPublicSharingPolicyCommand command,
        IStorageLimitRepository limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure(QuotaErrors.NotProvisioned);

        if (command.Allow)
            limit.EnablePublicSharing();
        else
            limit.DisablePublicSharing();

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
