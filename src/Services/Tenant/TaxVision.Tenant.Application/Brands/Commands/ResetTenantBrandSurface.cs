using System.Linq;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>
/// Mantenimiento completo: vuelve una superficie entera al default del sistema — quita todos los
/// colores y todos los assets (borrando cada archivo de CloudStorage). Idempotente. Si quita el logo
/// del CRM, avisa a Scribe (mismo contrato que el modelo viejo).
/// </summary>
public sealed record ResetTenantBrandSurfaceCommand(Guid TenantId, BrandSurface Surface);

public static class ResetTenantBrandSurfaceHandler
{
    public static async Task<Result> Handle(
        ResetTenantBrandSurfaceCommand cmd,
        ITenantBrandRepository repo,
        ITenantBrandingCloudStorageClient client,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var brand = await repo.GetAsync(cmd.TenantId, cmd.Surface, ct);
        if (brand is null)
            return Result.Success(); // ya está en default

        // Snapshot antes de mutar: se necesitan los fileId para borrar en CloudStorage.
        var assets = brand.Assets.Select(a => new { a.Key, a.FileId }).ToArray();
        foreach (var asset in assets)
        {
            var deleteResult = await client.DeleteAsync(cmd.TenantId, asset.FileId, ct);
            if (deleteResult.IsFailure)
                return deleteResult;

            brand.RemoveAsset(asset.Key);
        }

        brand.ResetColors();

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);

        foreach (var asset in assets)
            await BrandLogoEvents.PublishRemovedIfCrmLogoAsync(bus, correlation, cmd.TenantId, cmd.Surface, asset.Key);

        return Result.Success();
    }
}
