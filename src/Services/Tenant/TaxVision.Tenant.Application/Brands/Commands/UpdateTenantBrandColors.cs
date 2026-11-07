using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>
/// Patch de colores de una superficie. Un token en <c>null</c> = volver al default para ESE token
/// (se quita); no-null = fijarlo. Atómico: si algún hex no-null es inválido, no se aplica nada
/// (se valida antes de mutar).
/// </summary>
public sealed record UpdateTenantBrandColorsCommand(
    Guid TenantId,
    BrandSurface Surface,
    string? PrimaryColorHex,
    string? AccentColorHex
);

public static class UpdateTenantBrandColorsHandler
{
    public static async Task<Result> Handle(
        UpdateTenantBrandColorsCommand cmd,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        // Validación previa para atomicidad: ningún hex se aplica si otro es inválido.
        if (Invalid(cmd.PrimaryColorHex, out var primaryError))
            return Result.Failure(primaryError);
        if (Invalid(cmd.AccentColorHex, out var accentError))
            return Result.Failure(accentError);

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var brand = await BrandCommandSupport.GetOrCreateAsync(repo, cmd.TenantId, cmd.Surface, nowUtc, ct);

        Apply(brand, BrandColorToken.Primary, cmd.PrimaryColorHex, nowUtc);
        Apply(brand, BrandColorToken.Accent, cmd.AccentColorHex, nowUtc);

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.RequestInvalidationAsync(bus, cmd.TenantId, cmd.Surface);
        return Result.Success();
    }

    private static void Apply(Domain.Brands.TenantBrand brand, BrandColorToken token, string? hex, DateTime nowUtc)
    {
        if (hex is null)
            brand.RemoveColor(token, nowUtc);
        else
            brand.SetColor(token, hex, nowUtc); // ya validado arriba, no puede fallar
    }

    private static bool Invalid(string? hex, out Error error)
    {
        error = default!;
        if (hex is null)
            return false;

        var parsed = HexColor.Create(hex);
        if (parsed.IsSuccess)
            return false;

        error = parsed.Error;
        return true;
    }
}
