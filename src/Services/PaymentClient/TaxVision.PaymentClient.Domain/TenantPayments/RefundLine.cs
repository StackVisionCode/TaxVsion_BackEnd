using BuildingBlocks.Domain;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.TenantPayments;

/// <summary>Un reembolso (parcial o que completa el total) aplicado sobre un
/// <see cref="TenantPayment"/>. Entidad hija — requiere <c>ValueGeneratedNever()</c>. La suma
/// de <see cref="Amount"/> de todas las líneas nunca puede exceder el monto original del
/// pago; esa invariante la garantiza <see cref="TenantPayment.RefundPartial"/>.</summary>
public sealed class RefundLine : BaseEntity
{
    public Guid TenantPaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Reason { get; private set; } = default!;
    public string? ExternalRefundReference { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RefundedAtUtc { get; private set; }

    private RefundLine() { }

    public static RefundLine Create(
        Guid tenantPaymentId, Guid tenantId, Money amount, string reason, Guid requestedByUserId, DateTime nowUtc) =>
        new()
        {
            TenantPaymentId = tenantPaymentId,
            TenantId = tenantId,
            Amount = amount,
            Reason = reason,
            RequestedByUserId = requestedByUserId,
            RefundedAtUtc = nowUtc,
        };

    public void AttachExternalReference(string externalRefundReference) =>
        ExternalRefundReference = externalRefundReference;
}
