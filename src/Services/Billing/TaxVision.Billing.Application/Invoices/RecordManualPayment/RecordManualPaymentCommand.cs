using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.RecordManualPayment;

/// <summary>Registra un pago MANUAL/offline de una factura (efectivo, cheque, transferencia, otro) —
/// el tenant confirma que cobró por fuera del sistema. Marca la factura Paid (idempotente). No pasa por
/// PaymentClient ni Stripe.</summary>
public sealed record RecordManualPaymentCommand(
    Guid TenantId,
    Guid InvoiceId,
    string Method,
    long? AmountCents,
    DateTime? PaidAtUtc,
    Guid ActorUserId
);

public sealed record RecordManualPaymentResult(Guid InvoiceId, string Status);

public static class RecordManualPaymentHandler
{
    public static async Task<Result<RecordManualPaymentResult>> Handle(
        RecordManualPaymentCommand command,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<RecordManualPaymentResult>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        // Método manual: cualquier valor no-electrónico del enum; default Other si no parsea.
        if (!Enum.TryParse<PaymentMethod>(command.Method, ignoreCase: true, out var method))
            method = PaymentMethod.Other;

        var amountCents = command.AmountCents is > 0 ? command.AmountCents.Value : invoice.Total.AmountCents;
        var paidAtUtc = command.PaidAtUtc ?? clock.GetUtcNow().UtcDateTime;

        var wasPaid = invoice.Status == InvoiceStatus.Paid;
        var result = invoice.MarkPaid(amountCents, invoice.Currency, paidAtUtc, method);
        if (result.IsFailure)
            return Result.Failure<RecordManualPaymentResult>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // Si quedó pagada, regenerar el PDF con estado Paid → sale con la marca de agua "Pagado" + recibo.
        if (invoice.Status == InvoiceStatus.Paid && !wasPaid)
        {
            bus.TenantId = command.TenantId.ToString();
            await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, command.InvoiceId));
        }

        return Result.Success(new RecordManualPaymentResult(invoice.Id, invoice.Status.ToString()));
    }
}
