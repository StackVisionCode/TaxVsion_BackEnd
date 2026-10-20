using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payables.IntegrationEvents;

/// <summary>
/// Settles the payable and revokes its active payment link.
/// </summary>
public static class InvoicePaidConsumer
{
    public static async Task Handle(
        InvoicePaidIntegrationEvent evt,
        IPayableReferenceRepository payables,
        IPaymentLinkRepository links,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        var logger = loggerFactory.CreateLogger("PaymentClient.InvoicePaidConsumer");
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var externalRef = evt.InvoiceId.ToString();
            var payable = await payables.GetByExternalReferenceAsync(
                evt.TenantId,
                PaymentPurposeKind.InvoicePayment,
                externalRef,
                ct
            );
            if (payable is null)
            {
                logger.LogInformation("Invoice {InvoiceId} paid: no payable to settle.", evt.InvoiceId);
                return;
            }

            payable.Settle(DateTime.UtcNow);

            var activeLink = await links.GetActiveByExternalReferenceAsync(evt.TenantId, externalRef, ct);
            activeLink?.Revoke("Invoice paid", DateTime.UtcNow);

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Invoice {InvoiceId} paid: settled payable {PayableId} and revoked the active checkout link (if any).",
                evt.InvoiceId,
                payable.Id
            );
        }
    }
}
