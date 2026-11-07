using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
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
public sealed record ReissueInvoiceCommand(
    Guid TenantId,
    Guid InvoiceId,
    Guid ActorUserId,
    string? Reason,
    bool CanViewAll
);

public sealed record ReissueInvoiceResult(Guid OriginalInvoiceId, Guid ReplacementInvoiceId, string ReplacementStatus);

public static class ReissueInvoiceHandler
{
    public static async Task<Result<ReissueInvoiceResult>> Handle(
        ReissueInvoiceCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var original = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct, assignedTo);
        if (original is null)
            return Result.Failure<ReissueInvoiceResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        var nowUtc = clock.GetUtcNow().UtcDateTime;

        var drafted = original.CreateReplacementDraft(command.ActorUserId, nowUtc);
        if (drafted.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(drafted.Error);
        var replacement = drafted.Value;

        // El stock de la original se repone después del commit con SyncInvoiceStockCommand.
        var reissued = original.ReissueAs(replacement, command.Reason, nowUtc, command.ActorUserId);
        if (reissued.IsFailure)
            return Result.Failure<ReissueInvoiceResult>(reissued.Error);

        await invoices.AddAsync(replacement, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Post-commit (outbox y colas locales durables): la original revoca su enlace, regenera su PDF "Void" y repone
        // su stock, igual que una anulación normal. El reemplazo (borrador) no genera nada hasta que se emita.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(
            new InvoiceVoidedIntegrationEvent
            {
                TenantId = command.TenantId,
                CorrelationId = correlation.CorrelationId,
                InvoiceId = original.Id,
                InvoiceNumber = original.InvoiceNumber ?? string.Empty,
                Reason = reissued.Value,
            }
        );
        await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, original.Id));
        await bus.PublishAsync(new SyncInvoiceStockCommand(command.TenantId, original.Id));

        return Result.Success(new ReissueInvoiceResult(original.Id, replacement.Id, replacement.Status.ToString()));
    }
}
