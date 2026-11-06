using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Payments;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Fase 3: marca la factura <c>Paid</c> cuando PaymentClient confirma un cobro exitoso. Reacciona a
/// <see cref="TenantPaymentSucceededIntegrationEvent"/> (semántica "pagado"), NO a PaymentLinkUsed
/// ("redimido"). Solo cobros de tipo InvoicePayment. Correlaciona por el id de factura que viaja en
/// <c>ExternalReferenceId</c> y SUMA el pago. Idempotente por TenantPaymentId (no por estado): un pago
/// distinto sobre una factura Paid queda registrado como sobrepago. Un cobro que no se puede aplicar
/// (factura anulada, desconocida, otra moneda) queda como UnappliedPayment. IntegrationEventTenantMiddleware ya
/// restauró el tenant; GetByIdAsync usa IgnoreQueryFilters + tenantId explícito.
/// </summary>
public static class InvoicePaymentSucceededConsumer
{
    public static async Task Handle(
        TenantPaymentSucceededIntegrationEvent evt,
        IInvoiceRepository invoices,
        IUnappliedPaymentRepository unappliedPayments,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IBillingMetrics metrics,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.PurposeKind, "InvoicePayment", StringComparison.OrdinalIgnoreCase))
            return; // No es un cobro de factura; no nos concierne.

        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var rejection = new Rejection(evt, unappliedPayments, unitOfWork, bus, metrics, logger, correlationId);

            if (!Guid.TryParse(evt.ExternalReferenceId, out var invoiceId))
            {
                await rejection.RecordAsync(null, UnappliedPaymentReason.InvalidReference, null, ct);
                return;
            }

            var invoice = await invoices.GetByIdAsync(evt.TenantId, invoiceId, ct);
            if (invoice is null)
            {
                await rejection.RecordAsync(invoiceId, UnappliedPaymentReason.InvoiceNotFound, null, ct);
                return;
            }

            var amount = Money.Create(evt.AmountCents, evt.Currency);
            var paymentsBefore = invoice.Payments.Count;
            var result = amount.IsFailure
                ? Result.Failure(amount.Error)
                : invoice.ApplyPayment(
                    InvoicePaymentSource.Online,
                    evt.TenantPaymentId.ToString(),
                    amount.Value,
                    PaymentMethod.Online,
                    evt.PaidAtUtc,
                    Guid.Empty
                );
            if (result.IsFailure)
            {
                await rejection.RecordAsync(invoiceId, ReasonFor(invoice, result.Error), result.Error, ct);
                return;
            }

            // Mismo TenantPaymentId ya aplicado: no-op.
            if (invoice.Payments.Count == paymentsBefore)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            var applied = invoice.Payments.Last();
            if (applied.ExceedsDueCents > 0)
            {
                logger.LogWarning(
                    "Overpayment on invoice {InvoiceId}: payment {PaymentId} exceeds the total by {Exceeds} {Currency}.",
                    invoiceId,
                    evt.TenantPaymentId,
                    applied.ExceedsDueCents,
                    invoice.Currency
                );
                metrics.RecordOverpayment();
            }

            // Pagada: InvoicePaid y el PDF con la marca "Pagado" salen del evento de dominio (BB-1).

            // Cobro parcial: el enlace del cliente pasa a cobrar el saldo nuevo.
            if (invoice.Status == InvoiceStatus.PartiallyPaid)
            {
                bus.TenantId = evt.TenantId.ToString();
                await bus.PublishAsync(new EnsureInvoicePaymentLinkCommand(evt.TenantId, invoiceId));
            }

            logger.LogInformation(
                "Invoice {InvoiceId} marked {Status} from payment {PaymentId} ({Amount} {Currency}).",
                invoice.Id,
                invoice.Status,
                evt.TenantPaymentId,
                evt.AmountCents,
                evt.Currency
            );
        }
    }

    private static UnappliedPaymentReason ReasonFor(Invoice invoice, Error error) =>
        error.Code switch
        {
            "Billing.Invoice.NotPayable" when invoice.Status == InvoiceStatus.Voided =>
                UnappliedPaymentReason.InvoiceVoided,
            "Billing.Invoice.CurrencyMismatch" or "Billing.Money.InvalidCurrency" =>
                UnappliedPaymentReason.CurrencyMismatch,
            _ => UnappliedPaymentReason.NotPayable,
        };

    /// <summary>Cobro que no se pudo aplicar: queda como UnappliedPayment (guardado antes de publicar).</summary>
    private sealed class Rejection(
        TenantPaymentSucceededIntegrationEvent evt,
        IUnappliedPaymentRepository unappliedPayments,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IBillingMetrics metrics,
        ILogger logger,
        string correlationId
    )
    {
        public async Task RecordAsync(
            Guid? invoiceId,
            UnappliedPaymentReason reason,
            Error? error,
            CancellationToken ct
        )
        {
            // Reentrega del mismo cobro: ya está registrado.
            if (await unappliedPayments.ExistsAsync(evt.TenantId, evt.TenantPaymentId, ct))
                return;

            var payment = UnappliedPayment.Record(
                evt.TenantId,
                evt.TenantPaymentId,
                invoiceId,
                evt.ExternalReferenceId,
                evt.AmountCents,
                evt.Currency,
                reason,
                evt.PaidAtUtc
            );
            await unappliedPayments.AddAsync(payment, ct);
            await unitOfWork.SaveChangesAsync(ct);

            bus.TenantId = evt.TenantId.ToString();
            await bus.PublishAsync(
                new InvoicePaymentRejectedIntegrationEvent
                {
                    EventId = Guid.NewGuid(),
                    TenantId = evt.TenantId,
                    CorrelationId = correlationId,
                    AggregateId = payment.Id,
                    UnappliedPaymentId = payment.Id,
                    TenantPaymentId = evt.TenantPaymentId,
                    InvoiceId = invoiceId,
                    ExternalReferenceId = payment.ExternalReferenceId,
                    AmountCents = evt.AmountCents,
                    Currency = payment.Currency,
                    Reason = reason.ToString(),
                    PaidAtUtc = evt.PaidAtUtc,
                }
            );

            metrics.RecordPaymentRejected(reason.ToString());
            logger.LogError(
                "Payment {PaymentId} ({Amount} {Currency}) could not be applied to '{Ref}': {Reason} ({Code}). Recorded as unapplied payment {UnappliedPaymentId}.",
                evt.TenantPaymentId,
                evt.AmountCents,
                evt.Currency,
                evt.ExternalReferenceId,
                reason,
                error?.Code,
                payment.Id
            );
        }
    }
}
