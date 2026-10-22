namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Message-ID que pone Postmaster en cada envío: <c>pm.{sentMessageId:N}@{dominio del remitente}</c>.
/// Con este id cualquier proveedor nos dice a qué correo pertenece un evento.
/// </summary>
public sealed class OurMessageId
{
    private const string Prefix = "pm.";

    public Guid SentMessageId { get; }
    public string Domain { get; }

    private OurMessageId(Guid sentMessageId, string domain)
    {
        SentMessageId = sentMessageId;
        Domain = domain;
    }

    /// <summary>Valor para <c>X-TaxVision-Message-Id</c>: lo conservan proveedores que reescriben el Message-ID.</summary>
    public string CorrelationHeaderValue => SentMessageId.ToString("N");

    /// <summary>El dominio sale del remitente para que el id se vea como del propio emisor.</summary>
    public static OurMessageId For(Guid sentMessageId, string fromAddress)
    {
        var at = fromAddress.LastIndexOf('@');
        var domain = at >= 0 && at < fromAddress.Length - 1 ? fromAddress[(at + 1)..] : "localhost";
        return new OurMessageId(sentMessageId, domain.ToLowerInvariant());
    }

    /// <summary>Acepta el id con o sin los signos de menor/mayor del header. Un id ajeno devuelve false.</summary>
    public static bool TryParse(string? value, out OurMessageId? messageId)
    {
        messageId = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
            trimmed = trimmed[1..^1];

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1 || !trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var localPart = trimmed[Prefix.Length..at];
        if (localPart.Length != 32 || !Guid.TryParseExact(localPart, "N", out var sentMessageId))
            return false;

        messageId = new OurMessageId(sentMessageId, trimmed[(at + 1)..].ToLowerInvariant());
        return true;
    }

    public static bool TryParseCorrelationHeader(string? value, out Guid sentMessageId)
    {
        sentMessageId = Guid.Empty;
        var trimmed = value?.Trim();
        return trimmed is { Length: 32 } && Guid.TryParseExact(trimmed, "N", out sentMessageId);
    }

    /// <summary>Sin signos de menor/mayor: así lo espera MimeKit en <c>MimeMessage.MessageId</c>.</summary>
    public override string ToString() => $"{Prefix}{SentMessageId:N}@{Domain}";

    public override bool Equals(object? obj) =>
        obj is OurMessageId other && SentMessageId == other.SentMessageId && Domain == other.Domain;

    public override int GetHashCode() => HashCode.Combine(SentMessageId, Domain);
}
