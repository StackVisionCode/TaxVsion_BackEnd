using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.EditInvoice;

/// <summary>Edita una factura (borrador o emitida, incluso con pagos; D-BIL-1). Si estaba emitida, verifica
/// el stock de las nuevas líneas (bloquea si falta) y, después del commit, reconcilia el stock, refresca el
/// ancla de cobro y regenera el PDF por comandos durables. Si la edición la deja pagada, InvoicePaid sale del evento de dominio.
/// El tenant y el actor salen del JWT.</summary>
public sealed record EditInvoiceCommand(
    Guid TenantId,
    Guid InvoiceId,
    Guid ActorUserId,
    InvoiceCustomerInput Customer,
    string Currency,
    IReadOnlyList<InvoiceLineInput> Lines,
    string? Notes,
    bool CanViewAll
);

public sealed record EditInvoiceResult(Guid InvoiceId, string Status);

public static class EditInvoiceHandler
{
    public static async Task<Result<EditInvoiceResult>> Handle(
        EditInvoiceCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        // Visibilidad por asignación: la misma que ya filtra la lectura. Escribir sobre la factura de
        // un cliente que no le toca no puede quedar abierto solo porque el permiso de módulo alcance.
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct, assignedTo);
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

        // Si estaba emitida, verificar (solo lectura) que el stock alcanza para las nuevas cantidades. Si
        // falta, bloquear: no se guarda nada (la edición en memoria se descarta al no hacer SaveChanges).
        if (wasIssued)
        {
            var saleLines = command
                .Lines.Where(l => l.CatalogItemId is { } cid && cid != Guid.Empty && l.Quantity > 0)
                .Select(l => new InvoiceSaleLine(l.CatalogItemId!.Value, l.Quantity))
                .ToList();
            var checkedStock = await inventory.CheckInvoiceSaleAsync(command.TenantId, invoice.Id, saleLines, ct);
            if (checkedStock.IsFailure)
                return Result.Failure<EditInvoiceResult>(checkedStock.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Emitida: después del commit (outbox) se reconcilia el stock y se refresca el enlace con el
        // saldo nuevo; EnsureInvoicePaymentLink encadena el PDF.
        if (wasIssued)
        {
            bus.TenantId = command.TenantId.ToString();
            await bus.PublishAsync(new SyncInvoiceStockCommand(command.TenantId, invoice.Id));
            await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, invoice.Id));
            // Si bajó el total y quedó cubierta, InvoicePaid sale del evento de dominio (BB-1).
        }

        return Result.Success(new EditInvoiceResult(invoice.Id, invoice.Status.ToString()));
    }
}
