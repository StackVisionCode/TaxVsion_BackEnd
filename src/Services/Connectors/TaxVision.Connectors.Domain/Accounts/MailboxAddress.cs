using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>
/// Dirección de un buzón: se valida y normaliza una sola vez (trim y minúsculas). La cuenta guarda <see cref="Value"/>,
/// igual que en Customer y Postmaster: la columna sigue siendo un string y las filas viejas se leen sin revalidar.
/// </summary>
public sealed record MailboxAddress
{
    public const int MaxLength = 320;

    public string Value { get; }

    private MailboxAddress(string value) => Value = value;

    public static Result<MailboxAddress> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<MailboxAddress>(
                new Error("TenantEmailAccount.EmailAddress", "Email address is required.")
            );

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxLength || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<MailboxAddress>(
                new Error("TenantEmailAccount.EmailAddress", $"'{raw}' is not a valid email address.")
            );

        return Result.Success(new MailboxAddress(Normalize(trimmed)));
    }

    /// <summary>Si el texto es esta dirección, sin distinguir mayúsculas ni espacios. Lo que no es una dirección no coincide.</summary>
    public bool Matches(string? raw) => Value == Normalize(raw);

    // La única regla de normalización del servicio.
    private static string Normalize(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

    public override string ToString() => Value;
}
