using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>
/// Único lugar que arma la señal "pagado". Sale del pago, no del link: se publica cada vez que un
/// <see cref="TenantPayment"/> llega a <c>Succeeded</c>, por cualquier camino. Publicar después de guardar.
/// </summary>
public static class TenantPaymentSucceededSignal
{
    public static TenantPaymentSucceededIntegrationEvent Build(
        TenantPayment payment,
        DateTime paidAtUtc,
        string? correlationId
    ) =>
        new()
        {
            TenantId = payment.TenantId,
            TenantPaymentId = payment.Id,
            ProviderCode = payment.ProviderCode.ToString(),
            PurposeKind = payment.Purpose.Kind.ToString(),
            ExternalReferenceId = payment.Purpose.ExternalReferenceId,
            AmountCents = payment.Amount.AmountCents,
            Currency = payment.Amount.Currency,
            PaidAtUtc = paidAtUtc,
            CorrelationId = correlationId,
        };
}
