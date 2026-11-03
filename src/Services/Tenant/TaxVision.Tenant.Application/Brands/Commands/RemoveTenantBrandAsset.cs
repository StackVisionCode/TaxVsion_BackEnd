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

/// <summary>Elimina un asset (logo o favicon): borra el archivo de CloudStorage y lo quita de la
/// marca. Idempotente: sin ese asset, es un no-op exitoso. Si era un logo (CRM o Portal), publica
/// TenantLogoRemoved con su superficie para que correos, PDF y certificados dejen de mostrarlo.</summary>
public sealed record RemoveTenantBrandAssetCommand(Guid TenantId, BrandSurface Surface, BrandAssetKey Key);

public static class RemoveTenantBrandAssetHandler
{
    public static async Task<Result> Handle(
        RemoveTenantBrandAssetCommand cmd,
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
        var asset = brand?.Assets.FirstOrDefault(a => a.Key == cmd.Key);
        if (brand is null || asset is null)
            return Result.Success();

        var deleteResult = await client.DeleteAsync(cmd.TenantId, asset.FileId, ct);
        if (deleteResult.IsFailure)
            return deleteResult;

        brand.RemoveAsset(cmd.Key);

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);
        await BrandLogoEvents.PublishRemovedIfLogoAsync(bus, correlation, cmd.TenantId, cmd.Surface, cmd.Key);
        return Result.Success();
    }
}
