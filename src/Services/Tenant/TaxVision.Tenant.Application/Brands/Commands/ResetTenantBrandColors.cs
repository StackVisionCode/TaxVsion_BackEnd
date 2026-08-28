using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>Quita todos los colores de una superficie: vuelve a los defaults del sistema. Idempotente.</summary>
public sealed record ResetTenantBrandColorsCommand(Guid TenantId, BrandSurface Surface);

public static class ResetTenantBrandColorsHandler
{
    public static async Task<Result> Handle(
        ResetTenantBrandColorsCommand cmd,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var brand = await repo.GetAsync(cmd.TenantId, cmd.Surface, ct);
        if (brand is null)
            return Result.Success(); // nada que resetear = ya está en default

        brand.ResetColors();

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);
        return Result.Success();
    }
}
