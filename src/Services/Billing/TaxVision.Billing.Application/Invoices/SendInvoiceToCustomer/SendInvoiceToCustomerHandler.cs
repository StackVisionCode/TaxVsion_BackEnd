using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.SendInvoiceToCustomer;

/// <summary>
/// Publica <c>billing.invoice_sent.v1</c> con lo que el correo necesita. Billing es el dueño de los
/// datos de la factura, así que los manda él — antes los componía el navegador.
/// </summary>
public static class SendInvoiceToCustomerHandler
{
    public static async Task<Result> Handle(
        SendInvoiceToCustomerCommand command,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        // Misma visibilidad por asignación que el resto de escrituras sobre la factura.
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct, assignedTo);
        if (invoice is null)
            return Result.Failure(new Error("Billing.Invoice.NotFound", "Invoice does not exist."));

        if (invoice.InvoiceNumber is null)
            return Result.Failure(new Error("Billing.Invoice.NotIssued", "Issue the invoice before sending it."));

        var email = invoice.Customer.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure(new Error("Billing.Invoice.CustomerHasNoEmail", "This client has no email on file."));

        await bus.PublishAsync(
            new InvoiceSentToCustomerIntegrationEvent
            {
                TenantId = command.TenantId,
                CorrelationId = correlation.CorrelationId,
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                CustomerEmail = email,
                CustomerName = invoice.Customer.Name,
                AmountDueCents = invoice.AmountDue.AmountCents,
                Currency = invoice.Currency,
                DueDateUtc = invoice.DueDateUtc,
                PaymentLink = invoice.ActivePaymentLink?.CheckoutUrl,
                PdfFileId = invoice.PdfFileId,
            }
        );

        return Result.Success();
    }
}
