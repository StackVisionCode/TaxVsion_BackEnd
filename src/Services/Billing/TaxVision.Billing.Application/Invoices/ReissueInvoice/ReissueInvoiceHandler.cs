using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.Invoices;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.ReissueInvoice;

/// <summary>
/// Reemisión enlazada (item 6.3): corregir una factura ya emitida/pagada NO se hace mutándola, sino
/// anulando la original y creando un REEMPLAZO enlazado. El reemplazo nace como BORRADOR (copia del
/// cliente/líneas/moneda/notas de la original) para que el usuario lo corrija y lo emita; el pago ya
/// cobrado se ARRASTRA como crédito y se aplica al emitir (queda Paid si cubre el total, o PartiallyPaid
/// si el total nuevo es mayor). La original queda anulada (repone stock + revoca su link de cobro), con
/// motivo que la enlaza al reemplazo, y ambas caras quedan cruzadas y auditadas. El tenant y el actor
/// salen del JWT.
/// </summary>
public sealed record ReissueInvoiceCommand(Guid TenantId, Guid InvoiceId, Guid ActorUserId, string? Reason);

public sealed record ReissueInvoiceResult(Guid OriginalInvoiceId, Guid ReplacementInvoiceId, string ReplacementStatus);

public static class ReissueInvoiceHandler
{
    public static async Task<Result<ReissueInvoiceResult>> Handle(
        ReissueInvoiceCommand command,
        IInvoiceRepository invoices,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var original = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (original is null)
            return Result.Failure<ReissueInvoiceResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        var nowUtc = clock.GetUtcNow().UtcDateTime;

        // 1) Crear el reemplazo como BORRADOR: copia fiel de cliente, moneda, líneas (con su CatalogItemId
        //    para que el descuento de stock se rehaga al emitir) y notas. El emisor se conserva tal cual.
        var draftLines = original
            .Lines.Select(l => new DraftInvoiceLine(
                l.Description,
                l.Quantity,
                l.UnitAmount.AmountCents,
                l.TaxBasisPoints,
                l.CatalogItemId
            ))
            .ToList();

        var replacementResult = Invoice.CreateDraft(
            command.TenantId,
            command.ActorUserId,
            original.Customer,
            original.Currency,
            draftLines,
            original.Notes,
            nowUtc,
            original.Issuer
        );
        if (replacementResult.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(replacementResult.Error);
        var replacement = replacementResult.Value;

        // 2) Enlazar ambas caras + arrastrar el crédito (el pago ya cobrado en la original).
        var linked = replacement.LinkAsReplacementFor(original.Id, original.AmountPaid.AmountCents, nowUtc);
        if (linked.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(linked.Error);

        var marked = original.MarkReplacedBy(replacement.Id, nowUtc);
        if (marked.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(marked.Error);

        // 3) Anular la original: reponer stock ANTES (reconciliación a vacío, idempotente). Un fallo aborta
        //    toda la reemisión (nada se persiste) para reintentar sin dejar stock/estado a medias.
        var restocked = await inventory.CommitInvoiceSaleAsync(command.TenantId, original.Id, [], ct);
        if (restocked.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(restocked.Error);

        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? "Replaced by a corrected invoice."
            : command.Reason.Trim();
        var voided = original.Void(reason, nowUtc, command.ActorUserId);
        if (voided.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(voided.Error);

        await invoices.AddAsync(replacement, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // 4) Post-commit (outbox durable): la original anulada revoca su payable/link y regenera su PDF con
        //    la marca "Void" — igual que una anulación normal. El reemplazo (borrador) no genera artefactos
        //    hasta que se emita.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(
            new InvoiceVoidedIntegrationEvent
            {
                TenantId = command.TenantId,
                InvoiceId = original.Id,
                InvoiceNumber = original.InvoiceNumber ?? string.Empty,
                Reason = reason,
            }
        );
        await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, original.Id));

        return Result.Success(
            new ReissueInvoiceResult(original.Id, replacement.Id, replacement.Status.ToString())
        );
    }
}
