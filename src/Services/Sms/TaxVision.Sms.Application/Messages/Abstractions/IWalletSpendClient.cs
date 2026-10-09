namespace TaxVision.Sms.Application.Messages.Abstractions;

/// <summary>Tipos de referencia que SMS usa al gastar contra el Wallet (opaco para el Wallet).</summary>
public static class WalletReferenceTypes
{
    public const string IndividualSms = "individual-sms";
}

/// <summary>Unidades a cobrar por canal. Para SMS individual: <see cref="Sms"/> = cantidad de mensajes.</summary>
public sealed record WalletUnitCounts(long Email, long Sms, long Push, long WhatsApp);

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
/// Puerto hacia el Wallet para cobrar envíos individuales de SMS (PEP money-OUT, 00_Plan §5). El Wallet es
/// <b>independiente</b>: trabaja sobre una referencia opaca (<c>referenceType="individual-sms"</c> + batchId).
/// Reservar antes de despachar (on-behalf-of el bearer del usuario, o M2M del tenant); liquidar con lo enviado.
/// Copia local del mismo puerto que usa Campaigns — cada consumidor tiene el suyo.
/// </summary>
public interface IWalletSpendClient
{
    Task<WalletReserveResult> ReserveAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct = default
    );

    Task SettleAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        int consumedUnits,
        string? callerBearerToken,
        CancellationToken ct = default
    );
}
