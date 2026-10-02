using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.ChangeInvoiceStatus;

/// <summary>
/// Cambio de estado MANUAL de una factura por un usuario autorizado (item 6.2). El dominio solo permite
/// transiciones legales (<c>InvoiceStatusTransitions</c>) — nunca any→any — y audita cada una. Las
/// transiciones con efectos colaterales (emitir/cobrar/anular) NO pasan por acá: tienen su propio comando.
/// El tenant y el actor salen del JWT.
/// </summary>
public sealed record ChangeInvoiceStatusCommand(
    Guid TenantId,
    Guid InvoiceId,
    string ToStatus,
    string? Reason,
    Guid ActorUserId
);

public sealed record ChangeInvoiceStatusResult(Guid InvoiceId, string Status);

public static class ChangeInvoiceStatusHandler
{
    public static async Task<Result<ChangeInvoiceStatusResult>> Handle(
        ChangeInvoiceStatusCommand command,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<InvoiceStatus>(command.ToStatus, ignoreCase: true, out var target))
            return Result.Failure<ChangeInvoiceStatusResult>(
                new Error("Billing.Invoice.UnknownStatus", $"'{command.ToStatus}' is not a valid invoice status.")
            );

        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<ChangeInvoiceStatusResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        var changed = invoice.ChangeStatus(target, command.ActorUserId, command.Reason, clock.GetUtcNow().UtcDateTime);
        if (changed.IsFailure)
            return Result.Failure<ChangeInvoiceStatusResult>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ChangeInvoiceStatusResult(invoice.Id, invoice.Status.ToString()));
    }
}
