using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Correo de contacto. Se guarda recortado, tal como se escribió; el código de error lo pone el campo.</summary>
public sealed partial record ContactEmail
{
    // Límite práctico de RFC 5321 para una dirección completa.
    public const int MaxLength = 254;

    public const string InvalidMessage = "Enter a valid email address, like office@yourfirm.com.";

    public string Value { get; }

    private ContactEmail(string value) => Value = value;

    public static Result<ContactEmail?> CreateOptional(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<ContactEmail?>(null);

        if (trimmed.Length > MaxLength || !Pattern().IsMatch(trimmed))
            return Result.Failure<ContactEmail?>(new Error(errorCode, InvalidMessage));

        return Result.Success<ContactEmail?>(new(trimmed));
    }

    public override string ToString() => Value;

    // Un solo @, sin espacios, y un dominio con al menos un punto que no quede al borde.
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex Pattern();
}
