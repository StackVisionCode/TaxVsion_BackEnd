using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

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
        ICacheService cache,
        CancellationToken ct
    )
    {
        // Validación previa para atomicidad: ningún hex se aplica si otro es inválido.
        if (Invalid(cmd.PrimaryColorHex, out var primaryError))
            return Result.Failure(primaryError);
        if (Invalid(cmd.AccentColorHex, out var accentError))
            return Result.Failure(accentError);

        var brand = await BrandCommandSupport.GetOrCreateAsync(repo, cmd.TenantId, cmd.Surface, ct);

        Apply(brand, BrandColorToken.Primary, cmd.PrimaryColorHex);
        Apply(brand, BrandColorToken.Accent, cmd.AccentColorHex);

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);
        return Result.Success();
    }

    private static void Apply(Domain.Brands.TenantBrand brand, BrandColorToken token, string? hex)
    {
        if (hex is null)
            brand.RemoveColor(token);
        else
            brand.SetColor(token, hex); // ya validado arriba, no puede fallar
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
