using System.Text.RegularExpressions;

namespace TaxVision.Wallet.Application.Wallet.Credits;

/// <summary>
/// Claves de operación deterministas del ledger para créditos y reversiones por referencia opaca. La clave
/// (con el índice único <c>(TenantId, OperationKey)</c>) es lo que hace idempotente un reintento.
/// </summary>
public static partial class WalletCreditOperationKeys
{
    public static string Credit(string referenceType, Guid referenceId) =>
        $"wallet-credit:{referenceType}:{referenceId:N}";

    public static string Reversal(string referenceType, Guid referenceId) =>
        $"wallet-clawback:{referenceType}:{referenceId:N}";

    /// <summary>Normaliza y valida el tipo de referencia (minúsculas, 1-40, letras/dígitos/. _ -).</summary>
    public static bool TryNormalizeReferenceType(string? raw, out string normalized)
    {
        normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return ReferenceTypePattern().IsMatch(normalized);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,39}$")]
    private static partial Regex ReferenceTypePattern();
}
