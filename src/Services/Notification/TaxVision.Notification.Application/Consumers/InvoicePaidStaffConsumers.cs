using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>Capacidad que debe tener quien recibe un aviso de facturas (la declara Auth en su catálogo; Notification la valida
/// contra la proyección local de permisos).</summary>
internal static class BillingCapabilities
{
    public const string InvoicesView = "invoicing.view";
}

/// <summary>
/// <c>billing.invoice.paid</c> — AL PERSONAL de la oficina: una factura quedó pagada (el cliente pagó por el enlace, o
/// alguien registró el pago). Antes nadie lo avisaba: había que recargar la lista para enterarse.
///
/// <para>Destinatarios: el dueño de la oficina más quienes se suscribieron a las alertas de facturación
/// (<c>notifications.billing.alerts</c>), con la capacidad de ver facturas — el mismo criterio que el resto de alertas de
/// oficina. Es otro aviso que el del cliente (<see cref="InvoiceSentConsumer"/> y el recibo): este es interno.</para>
/// </summary>
public static class InvoicePaidStaffConsumer
{
    private const string EventKey = "billing.invoice.paid.v1";

    public static async Task Handle(
        InvoicePaidIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
                evt.TenantId,
                NotificationPermissions.BillingAlerts,
                new RecipientValidation(RequiredCapability: BillingCapabilities.InvoicesView),
                recipientResolver,
                ct
            );
            if (recipients.Count == 0)
                return;

            var title = $"Factura {evt.InvoiceNumber} pagada";
            var body = BuildBody(evt);

            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    NotificationRecipient.ForUser(userId),
                    title,
                    NotificationCategory.Billing,
                    "billing.invoice_paid",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.BillingInvoicePaid,
                        userId,
                        evt.EventId,
                        EventKey,
                        evt.OccurredOn,
                        title,
                        FallbackBody: body,
                        ResourceId: evt.InvoiceId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["invoiceId"] = evt.InvoiceId.ToString() },
                        Params: new Dictionary<string, string>
                        {
                            ["invoiceNumber"] = evt.InvoiceNumber,
                            ["amount"] = FormatAmount(evt.AmountPaidCents, evt.Currency),
                            ["customerName"] = evt.CustomerName ?? string.Empty,
                        }
                    ),
                    ct: ct
                );
            }
        }
    }

    internal static string BuildBody(InvoicePaidIntegrationEvent evt)
    {
        var who = string.IsNullOrWhiteSpace(evt.CustomerName) ? "El cliente" : evt.CustomerName.Trim();
        var how = evt.PaymentSource switch
        {
            "Online" => "por el enlace de pago",
            "Manual" => "con un pago registrado a mano",
            _ => string.Empty,
        };
        var amount = FormatAmount(evt.AmountPaidCents, evt.Currency);
        return string.IsNullOrEmpty(how)
            ? $"{who} saldó la factura {evt.InvoiceNumber} ({amount})."
            : $"{who} pagó la factura {evt.InvoiceNumber} ({amount}) {how}.";
    }

    internal static string FormatAmount(long cents, string currency) =>
        $"{cents / 100m:N2} {currency.ToUpperInvariant()}";
}

/// <summary>
/// <c>billing.invoice.payment_rejected.v1</c> — AL PERSONAL: un cobro confirmado no se pudo aplicar a su factura (anulada,
/// desconocida u otra moneda) y quedó como pago sin aplicar. El dinero ya se movió: alguien tiene que resolverlo.
/// </summary>
public static class InvoicePaymentRejectedStaffConsumer
{
    private const string EventKey = "billing.invoice.payment_rejected.v1";

    public static async Task Handle(
        InvoicePaymentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
                evt.TenantId,
                NotificationPermissions.BillingAlerts,
                new RecipientValidation(RequiredCapability: BillingCapabilities.InvoicesView),
                recipientResolver,
                ct
            );
            if (recipients.Count == 0)
                return;

            var amount = InvoicePaidStaffConsumer.FormatAmount(evt.AmountCents, evt.Currency);
            var title = $"Cobro de {amount} sin aplicar";
            var body =
                $"Se cobró {amount} pero no se pudo aplicar a su factura ({evt.Reason}). Resuélvelo en Facturación.";

            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    NotificationRecipient.ForUser(userId),
                    title,
                    NotificationCategory.Billing,
                    "billing.invoice_payment_rejected",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.BillingInvoicePaymentRejected,
                        userId,
                        evt.EventId,
                        EventKey,
                        evt.OccurredOn,
                        title,
                        FallbackBody: body,
                        ResourceId: evt.InvoiceId?.ToString() ?? evt.UnappliedPaymentId.ToString(),
                        Params: new Dictionary<string, string> { ["amount"] = amount, ["reason"] = evt.Reason }
                    ),
                    ct: ct
                );
            }
        }
    }
}
