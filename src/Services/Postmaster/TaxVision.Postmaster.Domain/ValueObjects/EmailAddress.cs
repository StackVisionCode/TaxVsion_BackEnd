using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Dirección de correo validada y normalizada (minúsculas, sin espacios). Las entidades guardan
/// <see cref="Value"/>, igual que en Customer y Correspondence: la columna sigue siendo un string.
/// </summary>
public sealed record EmailAddress
{
    public const int MaxLength = 320;

    public string Value { get; }

    private EmailAddress(string value) => Value = value;

    public static Result<EmailAddress> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<EmailAddress>(new Error("EmailAddress.Required", "Email address is required."));

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxLength || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<EmailAddress>(
                new Error("EmailAddress.Invalid", $"'{raw}' is not a valid email address.")
            );

        return Result.Success(new EmailAddress(trimmed.ToLowerInvariant()));
    }

    public override string ToString() => Value;
}
