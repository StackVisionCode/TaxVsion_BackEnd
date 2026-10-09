using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>
/// Errores de dominio del monedero (mismo patrón que <c>NoteErrors</c> en el esqueleto Notes).
/// <see cref="NotFound"/> se usa en Application (post-fetch, guardrail 8), nunca dentro del aggregate.
/// </summary>
public static class WalletErrors
{
    public static readonly Error NotFound = new("Wallet.NotFound", "Monedero no encontrado.");
    public static readonly Error TenantRequired = new("Wallet.TenantRequired", "TenantId is required.");
    public static readonly Error CurrencyRequired = new("Wallet.CurrencyRequired", "La moneda es requerida.");
    public static readonly Error OperationKeyRequired = new(
        "Wallet.OperationKeyRequired",
        "La clave de operación (opKey) es requerida."
    );
    public static readonly Error AmountNotPositive = new(
        "Wallet.AmountNotPositive",
        "El monto debe ser mayor que cero."
    );
    public static readonly Error InsufficientFunds = new(
        "Wallet.InsufficientFunds",
        "Saldo insuficiente para completar la operación."
    );
    public static readonly Error InsufficientHold = new(
        "Wallet.InsufficientHold",
        "No hay suficiente saldo reservado (Held) para esta operación."
    );
    public static readonly Error Frozen = new(
        "Wallet.Frozen",
        "El monedero está congelado: no admite nuevas reservas."
    );
    public static readonly Error CurrencyMismatch = new(
        "Wallet.CurrencyMismatch",
        "La moneda de la operación no coincide con la del monedero."
    );
}
