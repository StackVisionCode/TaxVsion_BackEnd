using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Templates.ValueObjects;

/// <summary>
/// Rol semántico de un slot en una plantilla (ej. "PrimaryTaxpayer", "Spouse",
/// "Preparer"). Es un label descriptivo — no una taxonomía cerrada — porque cada
/// tenant define sus propios roles según sus procesos.
///
/// <para>
/// Reglas: trim + longitud, permite letras/números/espacio/guiones. Se preserva
/// la capitalización original para presentación.
/// </para>
/// </summary>
public sealed record TemplateSlotRole
{
    public const int MinLength = 2;
    public const int MaxLength = 60;

    public string Value { get; }

    private TemplateSlotRole(string value) => Value = value;

    public static Result<TemplateSlotRole> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<TemplateSlotRole>(
                new Error("Signature.Template.RoleEmpty", "Slot role is required.")
            );

        var trimmed = candidate.Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
            return Result.Failure<TemplateSlotRole>(
                new Error(
                    "Signature.Template.RoleLength",
                    $"Slot role must be between {MinLength} and {MaxLength} characters."
                )
            );

        if (!IsSafeRoleLabel(trimmed))
            return Result.Failure<TemplateSlotRole>(
                new Error(
                    "Signature.Template.RoleFormat",
                    "Slot role can only contain letters, digits, spaces and dashes."
                )
            );

        return Result.Success(new TemplateSlotRole(trimmed));
    }

    public override string ToString() => Value;

    private static bool IsSafeRoleLabel(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
                return false;
        }
        return true;
    }
}
