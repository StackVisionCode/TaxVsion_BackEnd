using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Categories;

/// <summary>
/// Categoría de firma definida por el tenant (14.5). Amplía el set de sistema
/// (<see cref="SignatureCategoryDefaults"/>) con nombres propios reutilizables. La categoría se
/// guarda en cada solicitud como texto congelado, así que renombrar/archivar aquí no reescribe el
/// histórico — esta entidad es solo el "pick-list" del tenant.
/// </summary>
public sealed class TenantSignatureCategory : TenantEntity
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 60;

    private TenantSignatureCategory() { }

    public string Name { get; private set; } = default!;

    /// <summary>Nombre normalizado (trim + espacios colapsados + mayúsculas) para deduplicar e indexar.</summary>
    public string NormalizedName { get; private set; } = default!;

    public bool IsArchived { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<TenantSignatureCategory> Create(Guid tenantId, Guid createdByUserId, string name)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantSignatureCategory>(
                new Error("Signature.Category.Tenant", "TenantId is required.")
            );
        if (createdByUserId == Guid.Empty)
            return Result.Failure<TenantSignatureCategory>(
                new Error("Signature.Category.CreatedBy", "CreatedByUserId is required.")
            );

        var validation = ValidateName(name, out var trimmed, out var normalized);
        if (validation.IsFailure)
            return Result.Failure<TenantSignatureCategory>(validation.Error);

        var now = DateTime.UtcNow;
        var category = new TenantSignatureCategory
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = createdByUserId,
            Name = trimmed,
            NormalizedName = normalized,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        category.SetTenant(tenantId);
        return Result.Success(category);
    }

    public Result Rename(string name)
    {
        var validation = ValidateName(name, out var trimmed, out var normalized);
        if (validation.IsFailure)
            return validation;

        Name = trimmed;
        NormalizedName = normalized;
        Touch();
        return Result.Success();
    }

    public Result Archive()
    {
        IsArchived = true;
        Touch();
        return Result.Success();
    }

    public Result Unarchive()
    {
        IsArchived = false;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    /// <summary>Normaliza un nombre a su forma canónica (trim + espacios colapsados + mayúsculas invariantes).</summary>
    public static string Normalize(string name) =>
        string.Join(
                ' ',
                (name ?? string.Empty).Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .ToUpperInvariant();

    private static Result ValidateName(string name, out string trimmed, out string normalized)
    {
        trimmed = (name ?? string.Empty).Trim();
        normalized = Normalize(trimmed);
        var normalizedName = normalized; // copia local: un out no se puede capturar en el lambda de abajo.

        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure(new Error("Signature.Category.Name", "Category name is required."));
        if (trimmed.Length is < MinNameLength or > MaxNameLength)
            return Result.Failure(
                new Error(
                    "Signature.Category.Name",
                    $"Name must be between {MinNameLength} and {MaxNameLength} characters."
                )
            );
        // No puede chocar con una categoría de sistema (Fiscal, Other, …).
        if (SignatureCategoryDefaults.Names.Any(n => Normalize(n) == normalizedName))
            return Result.Failure(
                new Error("Signature.Category.Reserved", "That name is a built-in category and cannot be reused.")
            );

        return Result.Success();
    }
}
