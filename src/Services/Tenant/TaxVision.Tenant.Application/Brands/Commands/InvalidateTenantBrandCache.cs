using BuildingBlocks.Caching;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>Se entrega después del commit: invalidar antes deja que otra lectura vuelva a cachear lo viejo 5 min.</summary>
public sealed record InvalidateTenantBrandCacheCommand(Guid TenantId, BrandSurface Surface);

public static class InvalidateTenantBrandCacheHandler
{
    public static Task Handle(InvalidateTenantBrandCacheCommand command, ICacheService cache, CancellationToken ct) =>
        cache.RemoveAsync(BrandCacheKeys.Brand(command.TenantId, command.Surface), ct);
}
