using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands;

public static class BrandCacheKeys
{
    /// <summary>Marca efectiva (ya resuelta con la cascada) de un tenant para una superficie.</summary>
    public static string Brand(Guid tenantId, BrandSurface surface) => $"tenant:brand:{tenantId}:{surface}";
}
