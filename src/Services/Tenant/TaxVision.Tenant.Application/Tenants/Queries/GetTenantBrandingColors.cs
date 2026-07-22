using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Queries;

public sealed record GetTenantBrandingColorsQuery(Guid TenantId);

/// <summary>Siempre trae los 4 colores completos (custom o default) — nunca un campo vacío.</summary>
public sealed record TenantBrandingColorsResponse(
    string PrimaryColor,
    string AccentColor,
    string BackgroundColor,
    string TextColor,
    bool IsCustomized
);

public static class GetTenantBrandingColorsHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public static async Task<Result<TenantBrandingColorsResponse>> Handle(
        GetTenantBrandingColorsQuery query,
        ITenantRepository repo,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var cacheKey = TenantBrandingCacheKeys.Colors(query.TenantId);
        var cached = await cache.GetOrCreateAsync(
            cacheKey,
            innerCt => LoadAsync(query.TenantId, repo, innerCt),
            CacheTtl,
            ct
        );

        return cached is null
            ? Result.Failure<TenantBrandingColorsResponse>(new Error("Tenant.NotFound", "Tenant not found."))
            : Result.Success(cached);
    }

    private static async Task<TenantBrandingColorsResponse?> LoadAsync(
        Guid tenantId,
        ITenantRepository repo,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return null;

        var palette = tenant.ResolveBrandingPalette();
        return new TenantBrandingColorsResponse(
            palette.PrimaryColor.Value,
            palette.AccentColor.Value,
            palette.BackgroundColor.Value,
            palette.TextColor.Value,
            palette.IsCustomized
        );
    }
}
