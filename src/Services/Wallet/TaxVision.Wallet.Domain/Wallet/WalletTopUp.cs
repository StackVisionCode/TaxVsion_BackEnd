using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>Estado de una orden de recarga (00_Plan §6).</summary>
public enum WalletTopUpStatus
{
    /// <summary>Creada; esperando el cobro de PaymentApp.</summary>
    Pending = 1,

    /// <summary>Cobro confirmado y saldo acreditado.</summary>
    Credited = 2,

    /// <summary>El cobro falló; no se acreditó.</summary>
    Failed = 3,
}

/// <summary>
/// Orden de recarga del monedero (00_Plan §6). La crea <c>POST /wallet/top-ups</c> en estado
/// <see cref="WalletTopUpStatus.Pending"/> y publica <c>WalletTopUpDueIntegrationEvent</c>; PaymentApp
/// cobra off-session y responde Succeeded/Failed → aquí pasa a <see cref="WalletTopUpStatus.Credited"/>
/// o <see cref="WalletTopUpStatus.Failed"/>. El importe se guarda en <b>centavos</b> (unidad del cobro);
/// al acreditar, Wallet convierte a micros (<c>×10_000</c>).
/// </summary>
public sealed class WalletTopUp : TenantEntity
{
    private WalletTopUp() { }

    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = Wallet.DefaultCurrency;
    public WalletTopUpStatus Status { get; private set; } = WalletTopUpStatus.Pending;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid RequestedByUserId { get; private set; }
    public Guid? SaaSPaymentId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public long AmountMicros => AmountCents * 10_000;

    public static Result<WalletTopUp> Create(
        Guid tenantId,
        long amountCents,
        string currency,
        Guid requestedByUserId
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WalletTopUp>(WalletErrors.TenantRequired);
        if (amountCents <= 0)
            return Result.Failure<WalletTopUp>(WalletErrors.AmountNotPositive);

        var id = Guid.NewGuid();
        var topUp = new WalletTopUp
        {
            Id = id,
            AmountCents = amountCents,
            Currency = string.IsNullOrWhiteSpace(currency) ? Wallet.DefaultCurrency : currency.Trim().ToUpperInvariant(),
            Status = WalletTopUpStatus.Pending,
            // Clave idempotente estable por orden: un reintento continúa la misma operación, no re-cobra.
            IdempotencyKey = $"wallet-topup:{id:N}",
            RequestedByUserId = requestedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        topUp.SetTenant(tenantId);
        return Result.Success(topUp);
    }

    public void MarkCredited(Guid saaSPaymentId)
    {
        if (Status == WalletTopUpStatus.Credited)
            return; // idempotente
        Status = WalletTopUpStatus.Credited;
        SaaSPaymentId = saaSPaymentId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        if (Status == WalletTopUpStatus.Credited)
            return; // un pago confirmado nunca se degrada a Failed
        Status = WalletTopUpStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Registro de dedupe de acreditaciones (00_Plan §3): único por <c>(SourceService, SaaSPaymentId)</c>.
/// Antes de acreditar una recarga confirmada, Wallet inserta este registro; si ya existe (reentrega del
/// evento, clave técnica distinta), la acreditación se omite — un mismo pago NUNCA acredita dos veces.
/// </summary>
public sealed class FundingCredit : TenantEntity
{
    private FundingCredit() { }

    public string SourceService { get; private set; } = string.Empty;
    public Guid SaaSPaymentId { get; private set; }
    public long AmountMicros { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static FundingCredit Create(Guid tenantId, string sourceService, Guid saaSPaymentId, long amountMicros)
    {
        var credit = new FundingCredit
        {
            Id = Guid.NewGuid(),
            SourceService = sourceService,
            SaaSPaymentId = saaSPaymentId,
            AmountMicros = amountMicros,
            CreatedAtUtc = DateTime.UtcNow,
        };
        credit.SetTenant(tenantId);
        return credit;
    }
}
