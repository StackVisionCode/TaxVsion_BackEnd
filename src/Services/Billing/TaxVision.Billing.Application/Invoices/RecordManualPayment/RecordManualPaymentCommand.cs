using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.RecordManualPayment;

/// <summary>Registra un pago MANUAL/offline de una factura (efectivo, cheque, transferencia, otro) —
/// el tenant confirma que cobró por fuera del sistema. Se suma a lo ya pagado; sin monto, cobra el saldo.
/// <see cref="ClientRequestId"/> (Idempotency-Key) evita registrar dos veces el mismo clic. No pasa por
/// PaymentClient ni Stripe.</summary>
public sealed record RecordManualPaymentCommand(
    Guid TenantId,
    Guid InvoiceId,
    string Method,
    long? AmountCents,
    DateTime? PaidAtUtc,
    Guid ActorUserId,
    bool CanViewAll,
    string? ClientRequestId = null
);

public sealed record RecordManualPaymentResult(Guid InvoiceId, string Status);

public static class RecordManualPaymentHandler
{
    public static async Task<Result<RecordManualPaymentResult>> Handle(
        RecordManualPaymentCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
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
            return Result.Failure<RecordManualPaymentResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        // Método manual: cualquier valor no-electrónico del enum; default Other si no parsea.
        if (!Enum.TryParse<PaymentMethod>(command.Method, ignoreCase: true, out var method))
            method = PaymentMethod.Other;

        var amountCents = command.AmountCents is > 0 ? command.AmountCents.Value : invoice.AmountDue.AmountCents;
        if (amountCents <= 0)
            return Result.Failure<RecordManualPaymentResult>(
                new Error("Billing.Invoice.NoBalanceDue", "This invoice has no balance due.")
            );
        var amount = Money.Create(amountCents, invoice.Currency);
        if (amount.IsFailure)
            return Result.Failure<RecordManualPaymentResult>(amount.Error);
        var paidAtUtc = command.PaidAtUtc ?? clock.GetUtcNow().UtcDateTime;
        var sourceReference = string.IsNullOrWhiteSpace(command.ClientRequestId)
            ? Guid.NewGuid().ToString()
            : command.ClientRequestId.Trim();

        var result = invoice.ApplyPayment(
            InvoicePaymentSource.Manual,
            sourceReference,
            amount.Value,
            method,
            paidAtUtc,
            command.ActorUserId
        );
        if (result.IsFailure)
            return Result.Failure<RecordManualPaymentResult>(result.Error);

        // Si quedó pagada, InvoicePaid y el PDF con la marca "Pagado" salen del evento de dominio (BB-1).
        await unitOfWork.SaveChangesAsync(ct);

        // Pago parcial: el enlace del cliente pasa a cobrar el saldo nuevo (idempotente; también en reenvíos).
        if (invoice.Status == InvoiceStatus.PartiallyPaid)
        {
            bus.TenantId = command.TenantId.ToString();
            await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(command.TenantId, command.InvoiceId));
        }

        return Result.Success(new RecordManualPaymentResult(invoice.Id, invoice.Status.ToString()));
    }
}
