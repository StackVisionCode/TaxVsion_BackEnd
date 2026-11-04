using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Nombre legal de la oficina (el registrado ante el estado y el IRS). Opcional; se guarda recortado.</summary>
public sealed record OfficeLegalName
{
    public const int MaxLength = 200;

    public static readonly Error TooLong = new(
        "Tenant.Profile.LegalName",
        $"The legal name cannot exceed {MaxLength} characters."
    );

    public string Value { get; }

    private OfficeLegalName(string value) => Value = value;

    public static Result<OfficeLegalName?> CreateOptional(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<OfficeLegalName?>(null);

        return trimmed.Length > MaxLength
            ? Result.Failure<OfficeLegalName?>(TooLong)
            : Result.Success<OfficeLegalName?>(new(trimmed));
    }

    public override string ToString() => Value;
}
