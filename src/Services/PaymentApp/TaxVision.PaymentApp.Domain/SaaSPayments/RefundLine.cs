using BuildingBlocks.Domain;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Un reembolso (parcial o que completa el total) aplicado sobre un <see cref="SaaSPayment"/>.
/// Entidad hija — su configuración EF requiere <c>ValueGeneratedNever()</c> (guardrail §49).
/// La suma de <see cref="Amount"/> de todas las líneas nunca puede exceder el monto original
/// del pago; esa invariante la garantiza <see cref="SaaSPayment.RefundPartial"/>, no esta clase.
/// </summary>
public sealed class RefundLine : BaseEntity
{
    public Guid SaaSPaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Reason { get; private set; } = default!;
    public string? ExternalRefundReference { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RefundedAtUtc { get; private set; }

    private RefundLine() { }

    public static RefundLine Create(
        Guid saaSPaymentId,
        Guid tenantId,
        Money amount,
        string reason,
        Guid requestedByUserId,
        DateTime nowUtc
    ) =>
        new()
        {
            SaaSPaymentId = saaSPaymentId,
            TenantId = tenantId,
            Amount = amount,
            Reason = reason,
            RequestedByUserId = requestedByUserId,
            RefundedAtUtc = nowUtc,
        };

    public void AttachExternalReference(string externalRefundReference) =>
        ExternalRefundReference = externalRefundReference;
}
