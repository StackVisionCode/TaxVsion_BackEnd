using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Growth.SharedKernel.Buyers;

/// <summary>
/// Email de un comprador normalizado para comparar (N11): una persona es un comprador aunque use alias. Nunca reemplaza
/// el email real (en Auth sigue igual) ni se guarda ni se loguea: solo se usa para calcular su <see cref="BuyerIdentity"/>.
/// </summary>
public sealed class NormalizedEmail
{
    public const int MaxLength = 254;

    private static readonly HashSet<string> GmailDomains = ["gmail.com", "googlemail.com"];
    private static readonly HashSet<string> YahooDashDomains = ["ymail.com", "rocketmail.com"];

    // Dominios que llegan al mismo buzón. Hotmail, Outlook, Live y MSN NO: son cuentas distintas.
    private static readonly Dictionary<string, string> SameMailboxDomains = new(StringComparer.Ordinal)
    {
        ["googlemail.com"] = "gmail.com",
        ["me.com"] = "icloud.com",
        ["mac.com"] = "icloud.com",
    };

    private NormalizedEmail(string localPart, string domain)
    {
        LocalPart = localPart;
        Domain = domain;
    }

    public string LocalPart { get; }
    public string Domain { get; }
    public string Value => $"{LocalPart}@{Domain}";

    public static Result<NormalizedEmail> Create(string? raw)
    {
        var text = (raw ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var at = text.LastIndexOf('@');
        if (text.Length is 0 or > MaxLength || at <= 0 || at == text.Length - 1 || text.Any(char.IsWhiteSpace))
            return Invalid();

        var local = text[..at];
        var domain = text[(at + 1)..];
        if (local.Contains('@') || !domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
            return Invalid();

        // Sub-direcciones: se quita desde el primer "+" en cualquier dominio.
        var plus = local.IndexOf('+');
        if (plus >= 0)
            local = local[..plus];

        if (GmailDomains.Contains(domain))
            local = local.Replace(".", string.Empty, StringComparison.Ordinal);

        if (IsYahoo(domain))
        {
            var dash = local.IndexOf('-');
            if (dash > 0)
                local = local[..dash];
        }

        if (SameMailboxDomains.TryGetValue(domain, out var mailbox))
            domain = mailbox;

        return local.Length == 0 ? Invalid() : Result.Success(new NormalizedEmail(local, domain));
    }

    // Nunca mostrar el valor: este objeto no debe acabar en logs ni en mensajes.
    public override string ToString() => "<normalized-email>";

    private static bool IsYahoo(string domain) =>
        YahooDashDomains.Contains(domain) || domain.StartsWith("yahoo.", StringComparison.Ordinal);

    private static Result<NormalizedEmail> Invalid() =>
        Result.Failure<NormalizedEmail>(new Error("Codes.Buyer.InvalidEmail", "The buyer email is not valid."));
}
