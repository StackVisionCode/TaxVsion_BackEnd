using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>F7 — a quién le llega la copia parcial al firmar: todos los firmantes o un set explícito.</summary>
public enum PartialCopyAudienceKind
{
    All = 0,
    Specific = 1,
}

/// <summary>
/// VO inmutable que decide si un firmante entra en la audiencia de la copia parcial.
/// Específica no acepta set vacío (sería "nadie": si no quieres copia, apaga el flag en la request).
/// </summary>
/// <remarks>
/// Persistencia: el storage es un CSV de GUIDs (<see cref="SpecificSignerIdsCsv"/>) para que EF lo
/// mapee a una sola columna. El set público se materializa desde ahí.
/// </remarks>
public sealed class PartialCopyAudience
{
    public PartialCopyAudienceKind Kind { get; private set; }

    /// <summary>Serializado para EF. CSV de GUIDs sin espacios. Vacío cuando Kind=All.</summary>
    public string SpecificSignerIdsCsv { get; private set; } = string.Empty;

    /// <summary>Set no serializado — se recomputa desde el CSV.</summary>
    public IReadOnlySet<Guid> SpecificSignerIds =>
        SpecificSignerIdsCsv.Length == 0
            ? EmptySet
            : new HashSet<Guid>(
                SpecificSignerIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)
            );

    private static readonly IReadOnlySet<Guid> EmptySet = new HashSet<Guid>();

    // Requerido por EF Core.
    private PartialCopyAudience() { }

    private PartialCopyAudience(PartialCopyAudienceKind kind, string csv)
    {
        Kind = kind;
        SpecificSignerIdsCsv = csv;
    }

    public static PartialCopyAudience All() => new(PartialCopyAudienceKind.All, string.Empty);

    public static Result<PartialCopyAudience> Specific(IEnumerable<Guid> signerIds)
    {
        if (signerIds is null)
            return Result.Failure<PartialCopyAudience>(
                new Error("Signature.PartialCopyAudience.Null", "SignerIds is required for Specific.")
            );

        var set = new HashSet<Guid>(signerIds);
        if (set.Count == 0)
            return Result.Failure<PartialCopyAudience>(
                new Error("Signature.PartialCopyAudience.Empty", "Specific audience must include at least one signer.")
            );
        if (set.Contains(Guid.Empty))
            return Result.Failure<PartialCopyAudience>(
                new Error("Signature.PartialCopyAudience.InvalidId", "SignerIds cannot contain Guid.Empty.")
            );

        return Result.Success<PartialCopyAudience>(
            new PartialCopyAudience(PartialCopyAudienceKind.Specific, string.Join(',', set))
        );
    }

    public bool Includes(Guid signerId) => Kind == PartialCopyAudienceKind.All || SpecificSignerIds.Contains(signerId);
}
