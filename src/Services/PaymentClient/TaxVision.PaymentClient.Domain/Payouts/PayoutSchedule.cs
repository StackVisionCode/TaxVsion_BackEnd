using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Payouts;

/// <summary>
/// Preferencia de payout de un <c>TenantConnectAccount</c> — no ejecuta nada por sí misma
/// (Stripe paga automáticamente según lo configurado del lado de Stripe), es el espejo local
/// que <c>PayoutsController</c> expone al tenant y el ledger de <see cref="PayoutScheduleItem"/>
/// que el webhook Connect alimenta. Único por <c>(TenantConnectAccountId)</c>.
/// </summary>
public sealed class PayoutSchedule : TenantEntity
{
    private readonly List<PayoutScheduleItem> _items = [];

    public Guid TenantConnectAccountId { get; private set; }
    public PayoutFrequency Frequency { get; private set; }

    /// <summary>Día de la semana (0-6, Weekly) o del mes (1-31, Monthly) — null para
    /// Manual/Daily, que no tienen ancla.</summary>
    public int? Anchor { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<PayoutScheduleItem> Items => _items;

    private PayoutSchedule() { }

    public static Result<PayoutSchedule> Create(
        Guid tenantId, Guid tenantConnectAccountId, PayoutFrequency frequency, int? anchor, string currency, Guid actorUserId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PayoutSchedule>(new Error("PayoutSchedule.InvalidTenant", "TenantId is required."));

        if (tenantConnectAccountId == Guid.Empty)
            return Result.Failure<PayoutSchedule>(new Error("PayoutSchedule.InvalidConnectAccount", "TenantConnectAccountId is required."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<PayoutSchedule>(new Error("PayoutSchedule.InvalidCurrency", "Currency must be a 3-letter ISO-4217 code."));

        var anchorResult = ValidateAnchor(frequency, anchor);
        if (anchorResult.IsFailure)
            return Result.Failure<PayoutSchedule>(anchorResult.Error);

        var schedule = new PayoutSchedule
        {
            TenantConnectAccountId = tenantConnectAccountId,
            Frequency = frequency,
            Anchor = anchor,
            Currency = currency.Trim().ToUpperInvariant(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            UpdatedBy = actorUserId,
        };
        schedule.SetTenant(tenantId);
        return Result.Success(schedule);
    }

    public Result UpdateFrequency(PayoutFrequency frequency, int? anchor, Guid actorUserId, DateTime nowUtc)
    {
        var anchorResult = ValidateAnchor(frequency, anchor);
        if (anchorResult.IsFailure)
            return anchorResult;

        Frequency = frequency;
        Anchor = anchor;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }

    public void RecordPayoutPaid(string providerPayoutReference, Money amount, DateTime occurredAtUtc) =>
        _items.Add(PayoutScheduleItem.RecordPaid(Id, TenantId, providerPayoutReference, amount, occurredAtUtc));

    public void RecordPayoutFailed(string providerPayoutReference, Money amount, string failureReason, DateTime occurredAtUtc) =>
        _items.Add(PayoutScheduleItem.RecordFailed(Id, TenantId, providerPayoutReference, amount, failureReason, occurredAtUtc));

    private static Result ValidateAnchor(PayoutFrequency frequency, int? anchor) => frequency switch
    {
        PayoutFrequency.Weekly when anchor is null or < 0 or > 6 =>
            Result.Failure(new Error("PayoutSchedule.InvalidAnchor", "Weekly schedules require an anchor between 0 (Sunday) and 6 (Saturday).")),
        PayoutFrequency.Monthly when anchor is null or < 1 or > 31 =>
            Result.Failure(new Error("PayoutSchedule.InvalidAnchor", "Monthly schedules require an anchor between 1 and 31.")),
        PayoutFrequency.Manual or PayoutFrequency.Daily when anchor is not null =>
            Result.Failure(new Error("PayoutSchedule.InvalidAnchor", "Manual and Daily schedules cannot have an anchor.")),
        _ => Result.Success(),
    };
}
