using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IssueInvoice;

public static class IssueInvoiceHandler
{
    public static async Task<Result<IssueInvoiceResult>> Handle(
        IssueInvoiceCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        IInvoiceNumberSequenceRepository sequences,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        // Visibilidad por asignación: la misma que ya filtra la lectura. Escribir sobre la factura de
        // un cliente que no le toca no puede quedar abierto solo porque el permiso de módulo alcance.
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct, assignedTo);
        if (invoice is null)
            return Result.Failure<IssueInvoiceResult>(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        // Verificación de stock BLOQUEANTE y de solo lectura (D-BIL-2 (a)): si falta stock o Inventory no
        // responde, NO se emite. El descuento real lo hace SyncInvoiceStockCommand después del commit.
        var saleLines = invoice
            .Lines.Where(l => l.CatalogItemId is { } cid && cid != Guid.Empty && l.Quantity > 0)
            .Select(l => new InvoiceSaleLine(l.CatalogItemId!.Value, l.Quantity))
            .ToList();
        if (saleLines.Count > 0)
        {
            var checkedStock = await inventory.CheckInvoiceSaleAsync(command.TenantId, invoice.Id, saleLines, ct);
            if (checkedStock.IsFailure)
                return Result.Failure<IssueInvoiceResult>(checkedStock.Error);
        }

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var periodKey = nowUtc.Year.ToString(CultureInfo.InvariantCulture);

        var sequence = await sequences.GetOrCreateAsync(command.TenantId, periodKey, ct);
        var number = sequence.Allocate();
        var invoiceNumber = $"INV-{periodKey}-{number:D5}";

        // El vencimiento sale del plazo congelado en la factura (sin plazo: el histórico de 30 días).
        var issueResult = invoice.Issue(invoiceNumber, nowUtc, invoice.DueDateFor(nowUtc), command.ActorUserId);
        if (issueResult.IsFailure)
            return Result.Failure<IssueInvoiceResult>(issueResult.Error);

        // Reemisión (item 6.3): si este borrador es un reemplazo, arrastra el pago ya cobrado en la original
        // → al emitir queda Paid (si cubre el total) o PartiallyPaid (si el total nuevo es mayor). No-op si no
        // hay crédito. La transición la audita ApplyPayment.
        var credited = invoice.ApplyCarriedCredit(nowUtc);
        if (credited.IsFailure)
            return Result.Failure<IssueInvoiceResult>(credited.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // Post-commit (outbox durable): asegurar el ancla de cobro y LUEGO generar el PDF, cada paso en
        // su propia transacción con retry propio (punto 7 del review). Sellar el tenant para que
        // LocalCommandTenantMiddleware lo restaure.
        bus.TenantId = command.TenantId.ToString();
        if (invoice.Status == InvoiceStatus.Paid)
            await bus.PublishAsync(InvoicePaidEventFactory.Create(invoice, correlation.CorrelationId));
        await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, command.InvoiceId));
        if (saleLines.Count > 0)
            await bus.PublishAsync(new SyncInvoiceStockCommand(command.TenantId, command.InvoiceId));

        return Result.Success(new IssueInvoiceResult(invoice.Id, invoiceNumber, invoice.Status.ToString()));
    }
}
