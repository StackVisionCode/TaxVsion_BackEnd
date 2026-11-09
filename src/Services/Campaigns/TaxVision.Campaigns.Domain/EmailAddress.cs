using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain;

/// <summary>
/// Dirección de correo validada y normalizada (trim y minúsculas). Las entidades guardan <see cref="Value"/>, igual que
/// en Customer y Postmaster: la columna sigue siendo un string y las filas viejas se leen sin revalidar.
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
            return Result.Failure<EmailAddress>(new Error("EmailAddress.Invalid", "Email address is not valid."));

        return Result.Success(new EmailAddress(trimmed.ToLowerInvariant()));
    }

    /// <summary>La forma guardada, o <c>null</c> si está vacía o no es válida.</summary>
    public static string? Normalize(string? raw)
    {
        var email = Create(raw);
        return email.IsSuccess ? email.Value.Value : null;
    }

    public override string ToString() => Value;
}
