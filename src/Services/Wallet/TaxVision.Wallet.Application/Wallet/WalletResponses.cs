using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet;

/// <summary>
/// Vista del saldo — respuesta de <c>GET /wallet</c> (00_Plan §8). Montos en <b>micros</b> (string en JSON
/// para preservar <c>long</c> en clientes JS — lo serializa el controller/options).
/// </summary>
public sealed record WalletView(
    long PostedMicros,
    long HeldMicros,
    long AvailableMicros,
    string Currency,
    string Status,
    DateTime UpdatedAtUtc
)
{
    public static WalletView From(Domain.Wallet.Wallet w) =>
        new(w.PostedMicros, w.HeldMicros, w.AvailableMicros, w.Currency, w.Status.ToString(), w.UpdatedAtUtc);

    /// <summary>Saldo cero para un tenant sin monedero aún (zero-balance view).</summary>
    public static WalletView Zero() =>
        new(0, 0, 0, Domain.Wallet.Wallet.DefaultCurrency, WalletStatus.Active.ToString(), DateTime.UtcNow);
}

/// <summary>
/// Respuesta de <c>POST /wallet/top-ups</c>: la orden recién creada + la URL del checkout hosteado del
/// proveedor a la que el front redirige. El saldo se acredita al confirmarse el pago (no acá).
/// </summary>
public sealed record TopUpCheckoutView(TopUpView TopUp, string CheckoutUrl, DateTime ExpiresAtUtc);

/// <summary>Vista de una orden de recarga — respuesta de <c>POST /wallet/top-ups</c> y <c>GET /wallet/top-ups/{id}</c>.</summary>
public sealed record TopUpView(
    Guid Id,
    long AmountCents,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static TopUpView From(WalletTopUp t) =>
        new(t.Id, t.AmountCents, t.Currency, t.Status.ToString(), t.CreatedAtUtc, t.UpdatedAtUtc);
}

/// <summary>Vista de un asiento del ledger (dos deltas) — item de <c>GET /wallet/transactions</c>.</summary>
public sealed record LedgerEntryView(
    Guid Id,
    string Movement,
    long DeltaPostedMicros,
    long DeltaHeldMicros,
    long PostedAfterMicros,
    long HeldAfterMicros,
    string OperationKey,
    Guid? ReferenceId,
    DateTime CreatedAtUtc
)
{
    public static LedgerEntryView From(LedgerEntry e) =>
        new(
            e.Id,
            e.Movement.ToString(),
            e.DeltaPostedMicros,
            e.DeltaHeldMicros,
            e.PostedAfterMicros,
            e.HeldAfterMicros,
            e.OperationKey,
            e.ReferenceId,
            e.CreatedAtUtc
        );
}
