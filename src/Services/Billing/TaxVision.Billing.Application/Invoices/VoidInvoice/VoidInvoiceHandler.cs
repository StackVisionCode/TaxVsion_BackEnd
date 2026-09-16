using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.VoidInvoice;

/// <summary>Anula una factura emitida/pagada: la pasa a Voided con motivo y REPONE el stock que se
/// descontó al emitir (reconciliación a cantidades vacías). El tenant y el actor salen del JWT.</summary>
public sealed record VoidInvoiceCommand(Guid TenantId, Guid InvoiceId, Guid ActorUserId, string? Reason);

public static class VoidInvoiceHandler
{
    public static async Task<Result> Handle(
        VoidInvoiceCommand command,
        IInvoiceRepository invoices,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        // Reponer el stock ANTES de anular: reconciliación a cantidades vacías (idempotente, nunca bloquea).
        // Un fallo (Inventory inalcanzable) aborta la anulación para reintentar sin dejar stock sin reponer.
        var restocked = await inventory.CommitInvoiceSaleAsync(
            command.TenantId,
            invoice.Id,
            [],
            ct
        );
        if (restocked.IsFailure)
            return restocked;

        var voided = invoice.Void(command.Reason, clock.GetUtcNow().UtcDateTime, command.ActorUserId);
        if (voided.IsFailure)
            return voided;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
