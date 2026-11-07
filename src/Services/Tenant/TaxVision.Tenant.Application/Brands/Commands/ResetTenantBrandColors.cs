using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>Quita todos los colores de una superficie: vuelve a los defaults del sistema. Idempotente.</summary>
public sealed record ResetTenantBrandColorsCommand(Guid TenantId, BrandSurface Surface);

public static class ResetTenantBrandColorsHandler
{
    public static async Task<Result> Handle(
        ResetTenantBrandColorsCommand cmd,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var brand = await repo.GetAsync(cmd.TenantId, cmd.Surface, ct);
        if (brand is null)
            return Result.Success(); // nada que resetear = ya está en default

        brand.ResetColors(clock.GetUtcNow().UtcDateTime);

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.RequestInvalidationAsync(bus, cmd.TenantId, cmd.Surface);
        return Result.Success();
    }
}
