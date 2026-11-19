using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Wallet.Domain.ValueObjects;

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

    /// <summary>Cobrado pero no acreditado (moneda/monto no cuadra o crédito rechazado); revisión humana.</summary>
    NeedsReview = 4,
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

    [UserReference(
        UserReferenceKind.Provenance,
        Note = "Quién pidió la recarga; el cobro es de la oficina y sigue igual."
    )]
    public Guid RequestedByUserId { get; private set; }
    public Guid? SaaSPaymentId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Tipo de origen con que el ledger guarda la acreditación de una recarga.</summary>
    public const string ReferenceType = "topup";

    /// <summary>true = la disparó el saldo bajo con el consentimiento guardado, no una persona en el checkout.</summary>
    public bool Automatic { get; private set; }

    /// <summary>Proveedor del cobro (Stripe, PayPal). Null en órdenes anteriores al dato.</summary>
    public string? Provider { get; private set; }

    /// <summary>Medio del checkout (Card…). Null en recargas automáticas y órdenes anteriores.</summary>
    public string? Method { get; private set; }

    /// <summary>Método guardado autorizado (solo automáticas).</summary>
    public string? PaymentMethodReference { get; private set; }

    /// <summary>Etiqueta del método autorizado ("Visa •••• 4242"), solo automáticas.</summary>
    public string? MethodLabel { get; private set; }

    public long AmountMicros => AmountCents * 10_000;

    /// <summary>Cómo se pagó, para mostrar: la etiqueta guardada, "PayPal" o el medio del checkout.</summary>
    public string? PaymentLabel =>
        MethodLabel
        ?? (string.Equals(Provider, "PayPal", StringComparison.OrdinalIgnoreCase) ? "PayPal" : Method ?? Provider);

    /// <summary>Lo que se acredita al monedero: el importe de la orden en micros, con su moneda.</summary>
    public Result<WalletMoney> CreditAmount() => WalletMoney.Create(AmountMicros, Currency);

    /// <summary>Recarga automática por saldo bajo, sobre el método que la oficina autorizó.</summary>
    public static Result<WalletTopUp> CreateAutomatic(
        Guid tenantId,
        long amountCents,
        string currency,
        Guid consentUserId,
        Settings.AutoRechargeMethod method
    )
    {
        var created = Create(tenantId, amountCents, currency, consentUserId, method.Provider, null);
        if (created.IsFailure)
            return created;
        created.Value.Automatic = true;
        created.Value.PaymentMethodReference = method.Reference;
        created.Value.MethodLabel = method.Label;
        return created;
    }

    public static Result<WalletTopUp> Create(
        Guid tenantId,
        long amountCents,
        string currency,
        Guid requestedByUserId,
        string? provider = null,
        string? method = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WalletTopUp>(WalletErrors.TenantRequired);
        if (amountCents <= 0)
            return Result.Failure<WalletTopUp>(WalletErrors.AmountNotPositive);

        // Solo la moneda del monedero: el crédito se asienta en micros de USD.
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? Wallet.DefaultCurrency
            : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency != Wallet.DefaultCurrency)
            return Result.Failure<WalletTopUp>(WalletErrors.CurrencyMismatch);

        var id = Guid.NewGuid();
        var topUp = new WalletTopUp
        {
            Id = id,
            AmountCents = amountCents,
            Currency = normalizedCurrency,
            Status = WalletTopUpStatus.Pending,
            // Clave idempotente estable por orden: un reintento continúa la misma operación, no re-cobra.
            IdempotencyKey = $"wallet-topup:{id:N}",
            RequestedByUserId = requestedByUserId,
            Provider = Clean(provider, 32),
            Method = Clean(method, 32),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        topUp.SetTenant(tenantId);
        return Result.Success(topUp);
    }

    private static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
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

    /// <summary>Cobrado pero no acreditado: queda visible para revisión humana. No pisa Credited.</summary>
    public void MarkNeedsReview(string reason)
    {
        if (Status == WalletTopUpStatus.Credited)
            return;
        Status = WalletTopUpStatus.NeedsReview;
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
