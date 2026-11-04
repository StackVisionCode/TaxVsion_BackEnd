namespace TaxVision.PaymentClient.Domain.TenantPayments;

/// <summary>
/// Quién pagó un <see cref="TenantPayment"/>: el email que escribió el cliente para el recibo y lo
/// que el proveedor devuelve del pagador (nombre, email, marca y últimos 4 de la tarjeta). Todo es
/// opcional y la validación es ligera: un dato que no cuadra se descarta, nunca hace fallar un cobro.
/// Es PII — no se loguea.
/// </summary>
public sealed record PayerDetails
{
    public const int EmailMaxLength = 320;
    public const int NameMaxLength = 200;
    public const int CardBrandMaxLength = 30;
    public const int CardLast4Length = 4;

    public string? ReceiptEmail { get; private init; }
    public string? PayerName { get; private init; }
    public string? PayerEmail { get; private init; }
    public string? CardBrand { get; private init; }
    public string? CardLast4 { get; private init; }

    private PayerDetails() { }

    /// <summary>Instancia nueva en cada llamada: EF no admite compartir un owned entre filas.</summary>
    public static PayerDetails None() => new();

    public static PayerDetails FromReceiptEmail(string? receiptEmail) =>
        new() { ReceiptEmail = NormalizeEmail(receiptEmail) };

    /// <summary>Suma lo que devolvió el proveedor; un dato ausente o inválido conserva el anterior.</summary>
    public PayerDetails WithProviderData(string? payerName, string? payerEmail, string? cardBrand, string? cardLast4) =>
        this with
        {
            PayerName = NormalizeText(payerName, NameMaxLength) ?? PayerName,
            PayerEmail = NormalizeEmail(payerEmail) ?? PayerEmail,
            CardBrand = NormalizeText(cardBrand, CardBrandMaxLength) ?? CardBrand,
            CardLast4 = NormalizeLast4(cardLast4) ?? CardLast4,
        };

    private static string? NormalizeText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength ? null : trimmed;
    }

    private static string? NormalizeEmail(string? value)
    {
        var trimmed = NormalizeText(value, EmailMaxLength);
        if (trimmed is null)
            return null;

        // Forma mínima local@dominio sin espacios; lo demás lo valida quien envía el recibo.
        var at = trimmed.IndexOf('@');
        var looksLikeEmail =
            at > 0 && at == trimmed.LastIndexOf('@') && at < trimmed.Length - 1 && !trimmed.Any(char.IsWhiteSpace);
        return looksLikeEmail ? trimmed : null;
    }

    private static string? NormalizeLast4(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: CardLast4Length } && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }
}
