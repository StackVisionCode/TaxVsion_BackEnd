using BuildingBlocks.Domain;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Brands;

/// <summary>
/// Un color de marca dentro de una <see cref="TenantBrand"/>. Entidad hija del agregado: solo el
/// agregado la crea o modifica (factory <c>internal</c>), nunca se manipula suelta. Denormaliza
/// <see cref="ITenantOwned.TenantId"/> como red de seguridad del filtro fail-closed del DbContext.
/// </summary>
public sealed class TenantBrandColor : TenantEntity
{
    private TenantBrandColor() { }

    public Guid TenantBrandId { get; private set; }
    public BrandColorToken Token { get; private set; }
    public HexColor Color { get; private set; } = default!;
    public DateTime UpdatedAtUtc { get; private set; }

    internal static TenantBrandColor Create(Guid tenantId, Guid brandId, BrandColorToken token, HexColor color)
    {
        var entity = new TenantBrandColor
        {
            Id = Guid.NewGuid(),
            TenantBrandId = brandId,
            Token = token,
            Color = color,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        entity.SetTenant(tenantId);
        return entity;
    }

    internal void Update(HexColor color)
    {
        Color = color;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
