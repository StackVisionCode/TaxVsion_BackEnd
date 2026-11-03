using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// EFIN (Electronic Filing Identification Number) de la oficina principal: exactamente 6 dígitos. Es un
/// dato sensible: solo existe en claro el tiempo justo para cifrarlo o para revelarlo a un administrador.
/// </summary>
public sealed record Efin
{
    public const int Length = 6;
    public const int VisibleDigits = 2;

    public static readonly Error Invalid = new("Tenant.Compliance.Efin", "The EFIN must have exactly 6 digits.");

    public string Value { get; }

    private Efin(string value) => Value = value;

    public static Result<Efin> Create(string? raw)
    {
        var trimmed = raw?.Trim();
        if (trimmed is null || trimmed.Length != Length)
            return Result.Failure<Efin>(Invalid);

        foreach (var c in trimmed)
        {
            if (!char.IsAsciiDigit(c))
                return Result.Failure<Efin>(Invalid);
        }
        return Result.Success(new Efin(trimmed));
    }

    /// <summary>Últimos dígitos que se pueden mostrar enmascarados ("••••23").</summary>
    public string LastDigits => Value[^VisibleDigits..];

    // Nunca se imprime en logs ni en excepciones.
    public override string ToString() => "••••" + LastDigits;
}
