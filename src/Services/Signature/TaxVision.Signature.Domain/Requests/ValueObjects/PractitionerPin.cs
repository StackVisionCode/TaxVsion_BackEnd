using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// PIN alfanumérico corto que el staff comparte con el firmante por canal fuera de
/// banda (llamada, sesión en oficina, etc.). El VO valida sólo el formato de entrada;
/// el hash se produce en la capa Infrastructure con PBKDF2 antes de guardarlo.
///
/// <para>
/// Reglas: 4–10 dígitos numéricos. Explícitamente sin letras/símbolos para minimizar
/// errores en el canal verbal (IRS Publication 1345 recomienda dígitos para el
/// Practitioner PIN de Form 8879).
/// </para>
/// </summary>
public sealed record PractitionerPin
{
    public const int MinLength = 4;
    public const int MaxLength = 10;

    public string Value { get; }

    private PractitionerPin(string value) => Value = value;

    public static Result<PractitionerPin> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<PractitionerPin>(
                new Error("Signature.PractitionerPin.Empty", "Practitioner PIN is required.")
            );

        var trimmed = candidate.Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
            return Result.Failure<PractitionerPin>(
                new Error(
                    "Signature.PractitionerPin.Length",
                    $"Practitioner PIN must be between {MinLength} and {MaxLength} digits."
                )
            );

        foreach (var c in trimmed)
        {
            if (!char.IsDigit(c))
                return Result.Failure<PractitionerPin>(
                    new Error("Signature.PractitionerPin.Format", "Practitioner PIN must contain only digits.")
                );
        }

        return Result.Success(new PractitionerPin(trimmed));
    }

    public override string ToString() => "***"; // no exponer el PIN en logs por accidente
}
