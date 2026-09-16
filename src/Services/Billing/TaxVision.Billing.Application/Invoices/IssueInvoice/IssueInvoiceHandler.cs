using System.Globalization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IssueInvoice;

public static class IssueInvoiceHandler
{
    private const int DefaultNetDays = 30;

    public static async Task<Result<IssueInvoiceResult>> Handle(
        IssueInvoiceCommand command,
        IInvoiceRepository invoices,
        IInvoiceNumberSequenceRepository sequences,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<IssueInvoiceResult>(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        // Fase 3 — descuento de stock BLOQUEANTE al emitir. Se envían las líneas trazadas a un ítem de
        // catálogo (productos y servicios por igual); Inventory ignora servicios/no-rastreados y solo
        // descuenta productos rastreados. Si falta stock (o Inventory no responde), NO se emite:
        // fail-closed. Es idempotente por factura, así que un reintento de emisión no vuelve a descontar.
        var saleLines = invoice
            .Lines.Where(l => l.CatalogItemId is { } cid && cid != Guid.Empty && l.Quantity > 0)
            .Select(l => new InvoiceSaleLine(l.CatalogItemId!.Value, l.Quantity))
            .ToList();
        if (saleLines.Count > 0)
        {
            var committed = await inventory.CommitInvoiceSaleAsync(command.TenantId, invoice.Id, saleLines, ct);
            if (committed.IsFailure)
                return Result.Failure<IssueInvoiceResult>(committed.Error);
        }

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var periodKey = nowUtc.Year.ToString(CultureInfo.InvariantCulture);

        var sequence = await sequences.GetOrCreateAsync(command.TenantId, periodKey, ct);
        var number = sequence.Allocate();
        var invoiceNumber = $"INV-{periodKey}-{number:D5}";

        var issueResult = invoice.Issue(invoiceNumber, nowUtc, nowUtc.AddDays(DefaultNetDays), command.ActorUserId);
        if (issueResult.IsFailure)
            return Result.Failure<IssueInvoiceResult>(issueResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // Post-commit (outbox durable): asegurar el ancla de cobro y LUEGO generar el PDF, cada paso en
        // su propia transacción con retry propio (punto 7 del review). Sellar el tenant para que
        // LocalCommandTenantMiddleware lo restaure.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, command.InvoiceId));

        return Result.Success(new IssueInvoiceResult(invoice.Id, invoiceNumber, invoice.Status.ToString()));
    }
}
