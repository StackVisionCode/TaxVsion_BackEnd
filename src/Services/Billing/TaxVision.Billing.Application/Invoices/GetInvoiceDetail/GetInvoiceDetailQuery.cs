using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.GetInvoiceDetail;

/// <summary>Lectura RICA de una factura (incluye cliente y líneas) para prellenar el formulario de
/// edición. El listado/summary no trae líneas; esto sí.</summary>
public sealed record GetInvoiceDetailQuery(Guid TenantId, Guid InvoiceId);

public sealed record InvoiceDetailCustomer(
    Guid CustomerId,
    string Name,
    string? Email,
    string? Phone,
    string? TaxId
);

public sealed record InvoiceDetailLine(
    string Description,
    int Quantity,
    long UnitAmountCents,
    int TaxBasisPoints,
    Guid? CatalogItemId
);

public sealed record InvoiceDetailResponse(
    Guid Id,
    string? InvoiceNumber,
    string Status,
    string Currency,
    string? Notes,
    InvoiceDetailCustomer Customer,
    IReadOnlyList<InvoiceDetailLine> Lines,
    long SubtotalCents,
    long TaxTotalCents,
    long TotalCents,
    long AmountPaidCents,
    bool IsEditable,
    bool IsVoidable,
    bool IsDeletable
);

public static class GetInvoiceDetailHandler
{
    public static async Task<Result<InvoiceDetailResponse>> Handle(
        GetInvoiceDetailQuery query,
        IInvoiceRepository invoices,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(query.TenantId, query.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<InvoiceDetailResponse>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        // Libertad total: se edita cualquier factura salvo una anulada (estado terminal).
        var editable = invoice.Status != Domain.ValueObjects.InvoiceStatus.Voided;
        var voidable = invoice.Status
            is Domain.ValueObjects.InvoiceStatus.Issued
                or Domain.ValueObjects.InvoiceStatus.Sent
                or Domain.ValueObjects.InvoiceStatus.PartiallyPaid
                or Domain.ValueObjects.InvoiceStatus.Paid;
        var deletable = invoice.Status == Domain.ValueObjects.InvoiceStatus.Draft;

        return Result.Success(
            new InvoiceDetailResponse(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.Status.ToString(),
                invoice.Currency,
                invoice.Notes,
                new InvoiceDetailCustomer(
                    invoice.Customer.CustomerId,
                    invoice.Customer.Name,
                    invoice.Customer.Email,
                    invoice.Customer.Phone,
                    invoice.Customer.TaxId
                ),
                invoice
                    .Lines.Select(l => new InvoiceDetailLine(
                        l.Description,
                        l.Quantity,
                        l.UnitAmount.AmountCents,
                        l.TaxBasisPoints,
                        l.CatalogItemId
                    ))
                    .ToList(),
                invoice.Subtotal.AmountCents,
                invoice.TaxTotal.AmountCents,
                invoice.Total.AmountCents,
                invoice.AmountPaid.AmountCents,
                editable,
                voidable,
                deletable
            )
        );
    }
}
