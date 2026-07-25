using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record ResetTenantBrandingColorsCommand(Guid TenantId);

/// <summary>Idempotente: no falla si la paleta ya estaba en default — mismo criterio que RemoveTenantLogo.</summary>
public static class ResetTenantBrandingColorsHandler
{
    public static async Task<Result> Handle(
        ResetTenantBrandingColorsCommand cmd,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return Result.Failure(new Error("Tenant.NotFound", "Tenant not found."));

        tenant.ResetBrandingColors();
        await unitOfWork.SaveChangesAsync(ct);
        await cache.RemoveAsync(TenantBrandingCacheKeys.Colors(cmd.TenantId), ct);
        return Result.Success();
    }
}
