using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.ValueObjects;

/// <summary>
/// Dirección de correo validada. <see cref="NormalizedValue"/> (trim + lowercase) es lo
/// que se persiste y se usa para matcheo determinístico contra remitentes entrantes.
/// </summary>
public sealed record EmailAddress
{
    public const int MaxLength = 320;

    public string Value { get; }
    public string NormalizedValue { get; }

    private EmailAddress(string value, string normalizedValue)
    {
        Value = value;
        NormalizedValue = normalizedValue;
    }

    public static Result<EmailAddress> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<EmailAddress>(new Error("EmailAddress.Required", "Email is required."));

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxLength || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<EmailAddress>(new Error("EmailAddress.Invalid", "Email is invalid."));

        return Result.Success(new EmailAddress(trimmed, trimmed.ToLowerInvariant()));
    }

    public override string ToString() => Value;
}
