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

        // Un remitente puede llegar como header "Nombre <a@b.com>" (así lo manda Gmail); se extrae la
        // dirección para que el matcheo determinístico contra CustomerEmailAddresses (email pelado)
        // funcione. Una dirección ya pelada pasa intacta.
        var address = ExtractAddress(raw.Trim());
        if (address.Length > MaxLength || !address.Contains('@') || address.StartsWith('@') || address.EndsWith('@'))
            return Result.Failure<EmailAddress>(new Error("EmailAddress.Invalid", "Email is invalid."));

        return Result.Success(new EmailAddress(address, address.ToLowerInvariant()));
    }

    /// <summary>"Nombre &lt;a@b.com&gt;" → "a@b.com"; una dirección sin ángulos se devuelve tal cual.</summary>
    private static string ExtractAddress(string raw)
    {
        var open = raw.LastIndexOf('<');
        var close = raw.LastIndexOf('>');
        return open >= 0 && close > open ? raw[(open + 1)..close].Trim() : raw;
    }

    public override string ToString() => Value;
}
