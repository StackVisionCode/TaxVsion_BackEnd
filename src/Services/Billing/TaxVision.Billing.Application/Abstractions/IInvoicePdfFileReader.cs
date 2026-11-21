namespace TaxVision.Billing.Application.Abstractions;

/// <summary>PDF de las facturas de una oficina, para re-declararlos en CloudStorage (backfill).</summary>
public interface IInvoicePdfFileReader
{
    /// <summary>Página estable (por id) de las facturas del tenant que tienen algún PDF.</summary>
    Task<IReadOnlyList<InvoicePdfFileRow>> ListPageAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct = default
    );

    /// <summary>Facturas del tenant cuyo PDF vigente o pagado está en <paramref name="fileIds"/>.</summary>
    Task<IReadOnlyList<InvoicePdfFileRow>> ListByFileIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    );
}

/// <summary>PDF vigente y PDF pagado de una factura, con su cliente.</summary>
public sealed record InvoicePdfFileRow(Guid InvoiceId, Guid CustomerId, Guid? PdfFileId, Guid? PaidPdfFileId);
