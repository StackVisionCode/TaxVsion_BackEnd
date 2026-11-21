using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>Declaración en CloudStorage del PDF de una factura: el vigente se lista, el reemplazado no.</summary>
public static class InvoicePdfSource
{
    public const string Service = "billing";
    public const string ResourceType = "invoice";
    public const string CurrentRole = "invoice_pdf";
    public const string SupersededRole = "invoice_pdf_superseded";

    public static FileSourceDeclaredIntegrationEvent Declare(
        Guid tenantId,
        Guid invoiceId,
        Guid customerId,
        Guid fileId,
        string role,
        DateTime nowUtc,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            OccurredOn = nowUtc,
            CorrelationId = correlationId,
            FileId = fileId,
            SourceService = Service,
            SourceResourceType = ResourceType,
            SourceResourceId = invoiceId,
            SourceRole = role,
            RelatedCustomerId = customerId == Guid.Empty ? null : customerId,
        };

    /// <summary>Una declaración por PDF vigente de la fila (backfill y reclamo de CloudStorage).</summary>
    public static IEnumerable<FileSourceDeclaredIntegrationEvent> DeclareCurrent(
        Guid tenantId,
        InvoicePdfFileRow row,
        DateTime nowUtc,
        string correlationId
    ) =>
        Current(row.PdfFileId, row.PaidPdfFileId)
            .Select(fileId =>
                Declare(tenantId, row.InvoiceId, row.CustomerId, fileId, CurrentRole, nowUtc, correlationId)
            );

    /// <summary>PDF vigentes de la factura: el actual y el pagado, sin repetir.</summary>
    public static IReadOnlyList<Guid> Current(Guid? pdfFileId, Guid? paidPdfFileId) =>
        new[] { pdfFileId, paidPdfFileId }.OfType<Guid>().Distinct().ToList();

    /// <summary>Los que eran vigentes antes y ya no lo son.</summary>
    public static IReadOnlyList<Guid> Superseded(IReadOnlyList<Guid> before, IReadOnlyList<Guid> after) =>
        before.Except(after).ToList();
}
