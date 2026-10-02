using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.VoidInvoice;

/// <summary>Anula una factura emitida/pagada: la pasa a Voided con motivo y REPONE el stock que se
/// descontó al emitir (reconciliación a cantidades vacías). El tenant y el actor salen del JWT.</summary>
public sealed record VoidInvoiceCommand(
    Guid TenantId,
    Guid InvoiceId,
    Guid ActorUserId,
    string? Reason,
    bool CanViewAll
);

public static class VoidInvoiceHandler
{
    public static async Task<Result> Handle(
        VoidInvoiceCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
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
            return Result.Failure(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        // Reponer el stock ANTES de anular: reconciliación a cantidades vacías (idempotente, nunca bloquea).
        // Un fallo (Inventory inalcanzable) aborta la anulación para reintentar sin dejar stock sin reponer.
        var restocked = await inventory.CommitInvoiceSaleAsync(command.TenantId, invoice.Id, [], ct);
        if (restocked.IsFailure)
            return restocked;

        var voided = invoice.Void(command.Reason, clock.GetUtcNow().UtcDateTime, command.ActorUserId);
        if (voided.IsFailure)
            return voided;

        // Anunciar la anulación para que PaymentClient REVOQUE el payable/link de pago (una factura anulada
        // no se debe poder pagar). Vía outbox durable: si PaymentClient está caído, se reintrega solo.
        await bus.PublishAsync(
            new InvoiceVoidedIntegrationEvent
            {
                TenantId = command.TenantId,
                CorrelationId = correlation.CorrelationId,
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber ?? string.Empty,
                Reason = command.Reason,
            }
        );

        // Regenerar el PDF para que salga con la marca de agua "Void" (el template ya la contempla por
        // estado). Sin esto, el PDF de una factura anulada seguiría mostrándose como pendiente.
        await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, invoice.Id));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
