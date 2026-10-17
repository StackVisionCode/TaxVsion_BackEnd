using System.Globalization;

namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — forma canónica del mensaje que entra al HMAC. Se usa en signer Y verifier,
/// así que vivir en un solo lugar es la única manera de que no divergan silenciosamente. Formato:
/// <c>v1\n{tenantId}\n{kind}\n{intentId}\n{amountCents}\n{currencyUpper}\n{signedAtUnix}</c>.
///
/// Notas sobre decisiones:
/// <list type="bullet">
///   <item>Separador <c>\n</c> para evitar colisiones con cualquier GUID/currency/entero.</item>
///   <item>GUID en formato <c>D</c> (con guiones, lowercase) — formato invariable de .NET.</item>
///   <item>Currency normalizada a UPPER + <see cref="CultureInfo.InvariantCulture"/>; "usd" y "USD" producen la misma firma.</item>
///   <item>Enum serializado por nombre (<c>Kind.ToString()</c>), NO por valor numérico — renombrar un miembro invalida firmas en vuelo.</item>
///   <item>Prefijo de versión <c>v1</c> para rotación futura del formato sin bump de secret.</item>
/// </list></summary>
public static class PurchaseIntentAmountCanonicalBuilder
{
    public const string Version = "v1";

    public static string Build(PurchaseIntentAmountInput input, long signedAtUnixSeconds) =>
        string.Join(
            '\n',
            Version,
            input.TenantId.ToString("D", CultureInfo.InvariantCulture),
            input.Kind.ToString(),
            input.IntentId.ToString("D", CultureInfo.InvariantCulture),
            input.AmountCents.ToString(CultureInfo.InvariantCulture),
            (input.Currency ?? string.Empty).ToUpperInvariant(),
            signedAtUnixSeconds.ToString(CultureInfo.InvariantCulture)
        );
}
