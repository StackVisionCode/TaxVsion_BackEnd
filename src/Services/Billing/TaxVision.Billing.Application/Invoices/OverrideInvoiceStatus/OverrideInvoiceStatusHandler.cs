using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.OverrideInvoiceStatus;

/// <summary>
/// Corrección de estado a gusto de la oficina (cualquier estado → Draft / Issued / Sent / Paid / Voided), con motivo.
/// A diferencia de <c>ChangeInvoiceStatus</c> (matriz legal Issued⇄Sent) esto es la salida de escape; exige un permiso
/// propio y deja el rastro completo en la auditoría de estado.
/// </summary>
public sealed record OverrideInvoiceStatusCommand(
    Guid TenantId,
    Guid InvoiceId,
    string ToStatus,
    string? Reason,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>Un pago que se revirtió. <paramref name="NeedsProviderRefund"/>: era un cobro online, hay que devolverlo en el proveedor.</summary>
public sealed record ReversedPaymentInfo(
    Guid PaymentId,
    string Source,
    long AmountCents,
    string Currency,
    bool NeedsProviderRefund
);

public sealed record OverrideInvoiceStatusResult(
    Guid InvoiceId,
    string Status,
    bool Changed,
    IReadOnlyList<ReversedPaymentInfo> ReversedPayments,
    bool PaymentRecorded
);

public static class OverrideInvoiceStatusHandler
{
    public const string ReopenedAsDraftReason = "Reopened as a draft by the office.";

    public static async Task<Result<OverrideInvoiceStatusResult>> Handle(
        OverrideInvoiceStatusCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<InvoiceStatus>(command.ToStatus, ignoreCase: true, out var target))
            return Result.Failure<OverrideInvoiceStatusResult>(
                new Error("Billing.Invoice.UnknownStatus", $"'{command.ToStatus}' is not a valid invoice status.")
            );

        // Misma visibilidad por asignación que el resto de escrituras sobre la factura.
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct, assignedTo);
        if (invoice is null)
            return Result.Failure<OverrideInvoiceStatusResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        var overridden = invoice.OverrideStatus(
            target,
            command.Reason,
            clock.GetUtcNow().UtcDateTime,
            command.ActorUserId
        );
        if (overridden.IsFailure)
            return Result.Failure<OverrideInvoiceStatusResult>(overridden.Error);

        var outcome = overridden.Value;
        if (!outcome.Changed)
            return Result.Success(
                new OverrideInvoiceStatusResult(invoice.Id, invoice.Status.ToString(), false, [], false)
            );

        await unitOfWork.SaveChangesAsync(ct);

        // Después del commit (outbox): lo que cuelga del estado se reconcilia por sus vías de siempre.
        bus.TenantId = command.TenantId.ToString();

        switch (invoice.Status)
        {
            case InvoiceStatus.Voided:
                await PublishPayableRevokedAsync(
                    bus,
                    correlation,
                    command.TenantId,
                    invoice.Id,
                    invoice.InvoiceNumber,
                    command.Reason
                );
                // El PDF sale con la marca de agua "Void".
                await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, invoice.Id));
                break;
            case InvoiceStatus.Draft:
                // Un borrador no se puede pagar: se retira el cobro (la reemisión lo reinstala con la misma URL).
                await PublishPayableRevokedAsync(
                    bus,
                    correlation,
                    command.TenantId,
                    invoice.Id,
                    invoice.InvoiceNumber,
                    ReopenedAsDraftReason
                );
                break;
            default:
                // Cobrable otra vez (o pagada): asegura el cobro con el saldo vigente y, a continuación, regenera el PDF.
                // Reinstate: la oficina la volvió a dejar cobrable a propósito; si su cobro estaba revocado (anulada o
                // devuelta a borrador) se reinstala con la misma URL estable.
                await bus.PublishAsync(
                    new EnsureInvoicePaymentLinkCommand(command.TenantId, invoice.Id, Reinstate: true)
                );
                break;
        }

        // El stock depende del estado (Draft/Voided lo reponen, el resto lo descuenta): idempotente.
        await bus.PublishAsync(new SyncInvoiceStockCommand(command.TenantId, invoice.Id));

        var reversed = outcome
            .ReversedPayments.Select(p => new ReversedPaymentInfo(
                p.Id,
                p.Source.ToString(),
                p.Amount.AmountCents,
                p.Amount.Currency,
                p.NeedsProviderRefund
            ))
            .ToList();

        return Result.Success(
            new OverrideInvoiceStatusResult(
                invoice.Id,
                invoice.Status.ToString(),
                true,
                reversed,
                outcome.PaymentRecorded
            )
        );
    }

    // PaymentClient revoca el payable y su enlace al recibirlo. Es el mismo aviso de la anulación: lo único que
    // importa allí es "esta factura ya no se puede pagar".
    private static ValueTask PublishPayableRevokedAsync(
        IMessageBus bus,
        ICorrelationContext correlation,
        Guid tenantId,
        Guid invoiceId,
        string? invoiceNumber,
        string? reason
    ) =>
        bus.PublishAsync(
            new InvoiceVoidedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlation.CorrelationId,
                InvoiceId = invoiceId,
                InvoiceNumber = invoiceNumber ?? string.Empty,
                Reason = reason,
            }
        );
}
