using BuildingBlocks.Domain;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Payouts;

/// <summary>Registro append-only de un payout individual que Stripe ya ejecutó — Stripe
/// maneja el payout en sí (§19.1: "Payouts los maneja Stripe automáticamente al tenant"),
/// esto es solo el ledger que <c>ProcessConnectWebhookHandler</c> alimenta desde
/// <c>payout.paid</c>/<c>payout.failed</c>. Entidad hija de <see cref="PayoutSchedule"/>: su
/// configuración EF requiere <c>ValueGeneratedNever()</c>.</summary>
public sealed class PayoutScheduleItem : BaseEntity
{
    public Guid PayoutScheduleId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ProviderPayoutReference { get; private set; } = default!;
    public Money Amount { get; private set; } = null!;
    public PayoutScheduleItemStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    private PayoutScheduleItem() { }

    public static PayoutScheduleItem RecordPaid(
        Guid payoutScheduleId, Guid tenantId, string providerPayoutReference, Money amount, DateTime occurredAtUtc) =>
        new()
        {
            PayoutScheduleId = payoutScheduleId,
            TenantId = tenantId,
            ProviderPayoutReference = providerPayoutReference,
            Amount = amount,
            Status = PayoutScheduleItemStatus.Paid,
            OccurredAtUtc = occurredAtUtc,
        };

    public static PayoutScheduleItem RecordFailed(
        Guid payoutScheduleId, Guid tenantId, string providerPayoutReference, Money amount, string failureReason, DateTime occurredAtUtc) =>
        new()
        {
            PayoutScheduleId = payoutScheduleId,
            TenantId = tenantId,
            ProviderPayoutReference = providerPayoutReference,
            Amount = amount,
            Status = PayoutScheduleItemStatus.Failed,
            FailureReason = failureReason,
            OccurredAtUtc = occurredAtUtc,
        };
}
