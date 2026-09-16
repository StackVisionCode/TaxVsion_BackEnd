using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.EditInvoice;

/// <summary>Edita una factura editable (borrador, o emitida/enviada AÚN SIN PAGOS). Si estaba emitida,
/// reconcilia el stock a las nuevas líneas de producto (bloquea si falta), refresca el ancla de cobro con
/// el nuevo total y regenera el PDF. El tenant y el actor salen del JWT.</summary>
public sealed record EditInvoiceCommand(
    Guid TenantId,
    Guid InvoiceId,
    Guid ActorUserId,
    InvoiceCustomerInput Customer,
    string Currency,
    IReadOnlyList<InvoiceLineInput> Lines,
    string? Notes
);

public sealed record EditInvoiceResult(Guid InvoiceId, string Status);

public static class EditInvoiceHandler
{
    public static async Task<Result<EditInvoiceResult>> Handle(
        EditInvoiceCommand command,
        IInvoiceRepository invoices,
        IInventoryStockClient inventory,
        IInvoicePaymentLinkClient paymentLinks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<EditInvoiceResult>(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        // Toda factura ya EMITIDA (Issued/Sent/PartiallyPaid/Paid) descontó stock al emitir y tiene ancla
        // de cobro + PDF: hay que reconciliar. Solo un borrador no tocó nada aún.
        var wasIssued = invoice.Status is not InvoiceStatus.Draft;

        var customer = new CustomerSnapshot(
            command.Customer.CustomerId,
            command.Customer.Name,
            command.Customer.Email,
            command.Customer.Phone,
            command.Customer.TaxId,
            command.Customer.Billing is null
                ? null
                : new Address(
                    command.Customer.Billing.Line1,
                    command.Customer.Billing.Line2,
                    command.Customer.Billing.City,
                    command.Customer.Billing.State,
                    command.Customer.Billing.Zip,
                    command.Customer.Billing.Country
                )
        );

        var draftLines = command
            .Lines.Select(l => new DraftInvoiceLine(
                l.Description,
                l.Quantity,
                l.UnitAmountCents,
                l.TaxBasisPoints,
                l.CatalogItemId
            ))
            .ToList();

        // Validar y aplicar la edición EN MEMORIA (no se persiste todavía).
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var edited = invoice.Edit(customer, command.Currency, draftLines, command.Notes, nowUtc, command.ActorUserId);
        if (edited.IsFailure)
            return Result.Failure<EditInvoiceResult>(edited.Error);

        // Si estaba emitida, reconciliar el stock a las nuevas cantidades ANTES de persistir. Si falta
        // stock, bloquear: no se guarda nada (la edición en memoria se descarta al no hacer SaveChanges).
        if (wasIssued)
        {
            var saleLines = command
                .Lines.Where(l => l.CatalogItemId is { } cid && cid != Guid.Empty && l.Quantity > 0)
                .Select(l => new InvoiceSaleLine(l.CatalogItemId!.Value, l.Quantity))
                .ToList();
            var committed = await inventory.CommitInvoiceSaleAsync(command.TenantId, invoice.Id, saleLines, ct);
            if (committed.IsFailure)
                return Result.Failure<EditInvoiceResult>(committed.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Emitida: el total pudo cambiar → refrescar el ancla de cobro (monto) y regenerar el PDF.
        if (wasIssued)
        {
            bus.TenantId = command.TenantId.ToString();
            await paymentLinks.EnsurePayableAsync(
                invoice.Total.AmountCents,
                invoice.Currency,
                invoice.Id,
                command.TenantId,
                ct
            );
            await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, invoice.Id));
        }

        return Result.Success(new EditInvoiceResult(invoice.Id, invoice.Status.ToString()));
    }
}
