namespace TaxVision.Campaigns.Application.Runs.Abstractions;

/// <summary>Tipos de referencia que Campaigns usa al gastar contra el Wallet (opaco para el Wallet).</summary>
public static class WalletReferenceTypes
{
    public const string CampaignRun = "campaign-run";
}

/// <summary>Unidades a cobrar por canal (Σ destinatarios×canal). InApp va aparte: no se cobra.</summary>
public sealed record WalletUnitCounts(long Email, long Sms, long Push, long WhatsApp)
{
    public long BillableTotal => Email + Sms + Push + WhatsApp;
}

/// <summary>
/// Desenlace de reservar fondos en el Wallet.
/// <list type="bullet">
/// <item><see cref="Reachable"/> = false → el Wallet no respondió (fail-closed: el envío se bloquea).</item>
/// <item><see cref="Authorized"/> = true → fondos reservados; se puede despachar.</item>
/// <item><see cref="Authorized"/> = false → saldo insuficiente; <see cref="DeficitMicros"/> lleva el faltante.</item>
/// </list>
/// </summary>
public sealed record WalletReserveResult(
    bool Reachable,
    bool Authorized,
    long CostMicros,
    long AvailableMicros,
    long DeficitMicros,
    string Currency
)
{
    public static WalletReserveResult Unreachable() => new(false, false, 0, 0, 0, "USD");
}

/// <summary>
/// Puerto hacia el Wallet para gastar contra el saldo del tenant (PEP money-OUT, 00_Plan §5). El Wallet es
/// <b>independiente</b>: trabaja sobre una referencia opaca (<c>referenceType</c> + <c>referenceId</c>), así que
/// Campaigns es solo uno de sus consumidores. La implementación llama los endpoints internos
/// <c>POST /internal/wallet/reservations</c> (reservar, on-behalf-of el bearer del usuario) y
/// <c>POST /internal/wallet/reservations/settle</c> (liquidar, M2M del tenant).
/// </summary>
public interface IWalletSpendClient
{
    /// <summary>Reserva fondos ANTES del fan-out. Fail-closed: si el Wallet no responde → <see cref="WalletReserveResult.Unreachable"/>.</summary>
    Task<WalletReserveResult> ReserveAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct = default
    );

    /// <summary>
    /// Liquida la reserva al cerrar el consumidor: reporta cuántas unidades se usaron (el Wallet consume esa
    /// fracción y libera el resto). Idempotente. Lanza si el Wallet no está disponible (para que el consumer
    /// reintente); una referencia sin reserva (costo 0) es no-op.
    /// </summary>
    Task SettleAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        int consumedUnits,
        CancellationToken ct = default
    );
}
