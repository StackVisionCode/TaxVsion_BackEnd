using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.DeleteInvoice;

/// <summary>Borra (soft) un BORRADOR. Una factura emitida/pagada no se borra: se anula (void). El tenant
/// y el actor salen del JWT.</summary>
public sealed record DeleteInvoiceCommand(Guid TenantId, Guid InvoiceId, Guid ActorUserId);

public static class DeleteInvoiceHandler
{
    public static async Task<Result> Handle(
        DeleteInvoiceCommand command,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        var deleted = invoice.SoftDeleteDraft(clock.GetUtcNow().UtcDateTime, command.ActorUserId);
        if (deleted.IsFailure)
            return deleted;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
