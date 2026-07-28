using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Correlaciona el PDF generado por Documents con su factura. IntegrationEventTenantMiddleware ya
/// restauró el tenant del envelope; GetByIdAsync usa IgnoreQueryFilters + tenantId explícito. Solo
/// interesan las generaciones cuyo OwnerType es "Invoice".
/// </summary>
public static class DocumentGenerationCompletedConsumer
{
    public static async Task Handle(
        DocumentGenerationCompletedIntegrationEvent evt,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.OwnerType, "Invoice", StringComparison.OrdinalIgnoreCase))
            return;

        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            var invoice = await invoices.GetByIdAsync(evt.TenantId, evt.OwnerId, ct);
            if (invoice is null)
            {
                logger.LogWarning(
                    "DocumentGenerationCompleted for unknown invoice {InvoiceId} in tenant {TenantId}; ignoring.",
                    evt.OwnerId,
                    evt.TenantId
                );
                return;
            }

            invoice.AttachPdf(evt.FileId, clock.GetUtcNow().UtcDateTime);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Invoice {InvoiceId} linked to PDF FileId {FileId}.",
                invoice.Id,
                evt.FileId
            );
        }
    }
}
