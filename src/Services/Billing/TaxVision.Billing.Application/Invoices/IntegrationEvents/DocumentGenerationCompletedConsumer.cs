using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Correlaciona el PDF generado por Documents con su factura. IntegrationEventTenantMiddleware ya
/// restauró el tenant del envelope; GetByIdAsync usa IgnoreQueryFilters + tenantId explícito. Solo
/// interesan las generaciones cuyo OwnerType es "Invoice". Tras guardar, declara en CloudStorage el PDF
/// adoptado y degrada el que dejó de ser vigente; un PDF descartado por viejo no se declara.
/// </summary>
public static class DocumentGenerationCompletedConsumer
{
    public static async Task Handle(
        DocumentGenerationCompletedIntegrationEvent evt,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.OwnerType, "Invoice", StringComparison.OrdinalIgnoreCase))
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
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

            var before = InvoicePdfSource.Current(invoice.PdfFileId, invoice.PaidPdfFileId);
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            invoice.AttachPdf(evt.FileId, evt.IdempotencyKey, nowUtc);
            await unitOfWork.SaveChangesAsync(ct);
            await DeclareSourcesAsync(invoice, evt.FileId, before, bus, nowUtc, correlation.CorrelationId);

            logger.LogInformation("Invoice {InvoiceId} linked to PDF FileId {FileId}.", invoice.Id, evt.FileId);
        }
    }

    private static async Task DeclareSourcesAsync(
        Invoice invoice,
        Guid fileId,
        IReadOnlyList<Guid> before,
        IMessageBus bus,
        DateTime nowUtc,
        string correlationId
    )
    {
        var after = InvoicePdfSource.Current(invoice.PdfFileId, invoice.PaidPdfFileId);
        if (!after.Contains(fileId))
            return;

        await bus.PublishAsync(Declare(invoice, fileId, InvoicePdfSource.CurrentRole, nowUtc, correlationId));
        foreach (var superseded in InvoicePdfSource.Superseded(before, after))
            await bus.PublishAsync(
                Declare(invoice, superseded, InvoicePdfSource.SupersededRole, nowUtc, correlationId)
            );
    }

    private static FileSourceDeclaredIntegrationEvent Declare(
        Invoice invoice,
        Guid fileId,
        string role,
        DateTime nowUtc,
        string correlationId
    ) =>
        InvoicePdfSource.Declare(invoice.TenantId, invoice.Id, invoice.CustomerId, fileId, role, nowUtc, correlationId);
}
