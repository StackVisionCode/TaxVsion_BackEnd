using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Commands;

/// <summary>
/// Un campo en <c>null</c> significa "volver al default de la empresa para ese campo" (Tenant_Branding_Colors_Plan.md §5).
/// </summary>
public sealed record UpdateTenantBrandingColorsCommand(
    Guid TenantId,
    string? PrimaryColorHex,
    string? AccentColorHex,
    string? BackgroundColorHex,
    string? TextColorHex
);

public static class UpdateTenantBrandingColorsHandler
{
    public static async Task<Result> Handle(
        UpdateTenantBrandingColorsCommand cmd,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return Result.Failure(new Error("Tenant.NotFound", "Tenant not found."));

        var setResult = tenant.SetBrandingColors(
            cmd.PrimaryColorHex,
            cmd.AccentColorHex,
            cmd.BackgroundColorHex,
            cmd.TextColorHex
        );
        if (setResult.IsFailure)
            return setResult;

        await unitOfWork.SaveChangesAsync(ct);
        await cache.RemoveAsync(TenantBrandingCacheKeys.Colors(cmd.TenantId), ct);
        return Result.Success();
    }
}
