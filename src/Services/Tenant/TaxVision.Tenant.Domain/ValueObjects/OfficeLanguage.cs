using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Idioma por defecto de la oficina para lo que ve el cliente: <c>en</c> o <c>es</c>. Vacío = inglés.</summary>
public sealed record OfficeLanguage
{
    public const string English = "en";
    public const string Spanish = "es";

    public static readonly Error Invalid = new("Tenant.Profile.Language", "Default language must be \"en\" or \"es\".");

    public static readonly OfficeLanguage Default = new(English);

    public string Value { get; }

    private OfficeLanguage(string value) => Value = value;

    public static Result<OfficeLanguage> Create(string? raw)
    {
        var normalized = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return Result.Success(Default);

        return normalized is English or Spanish
            ? Result.Success(new OfficeLanguage(normalized))
            : Result.Failure<OfficeLanguage>(Invalid);
    }

    public override string ToString() => Value;
}
