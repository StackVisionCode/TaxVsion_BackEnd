using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>billing.invoice_sent.v1</c> — la oficina manda su factura al cliente.
///
/// <para>Se renderiza con <c>billing.invoice_sent</c>, la única plantilla sobre <c>tenant-base</c>:
/// el correo lo firma la oficina, así que lleva su cáscara y su logo, no los de la plataforma. El
/// idioma es el de la oficina, como el resto de plantillas de tenant.</para>
///
/// <para>El carril sale del render, no de acá: lo decide el layout. <c>TenantPreferred</c> elige el
/// transporte (buzón conectado de la oficina, y si no lo hay el del sistema en su nombre) y marca el
/// logo como del tenant.</para>
///
/// <para>Antes de 2026-10-03 esto no pasaba por acá: el CRM componía el HTML a mano y lo mandaba al
/// endpoint genérico de envío, por eso el cliente recibía texto plano sin marca.</para>
/// </summary>
public static class InvoiceSentConsumer
{
    // Scribe resuelve la plantilla por EVENT key; TemplateKey solo etiqueta el log de envio.
    private const string EventKey = "billing.invoice_sent.v1";
    private const string TemplateKey = "billing.invoice_sent";

    public static async Task Handle(
        InvoiceSentToCustomerIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICustomerContactPolicyRepository contactPolicies,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // T7.3: respeta ContactPolicy.Email del cliente si viene mapeado. Publicaciones
            // viejas sin CustomerId pasan directo (null → true), igual que un cliente sin fila.
            var emailAllowed = await CustomerContactPolicyFilter.AllowsForOptionalCustomerAsync(
                evt.TenantId,
                evt.CustomerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            );
            if (emailAllowed && !string.IsNullOrWhiteSpace(evt.CustomerEmail))
            {
                var render = (
                    await scribeClient.RenderAsync(
                        EventKey,
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["invoice_number"] = evt.InvoiceNumber,
                            ["customer_name"] = evt.CustomerName,
                            ["amount_due"] = FormatAmount(evt.AmountDueCents, evt.Currency),
                            ["due_date"] = evt.DueDateUtc?.ToString("MMM d, yyyy"),
                            ["payment_link"] = evt.PaymentLink,
                            ["has_pdf"] = evt.PdfFileId is not null,
                        },
                        ct
                    )
                ).EnsureRendered(EventKey);

                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: evt.CustomerEmail,
                        templateKey: TemplateKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlation.CorrelationId
                    ) with
                    {
                        AttachmentFileIds = evt.PdfFileId is { } pdf ? [pdf] : null,
                    },
                    ct
                );
            }

            // T7.4: fan-out in-app al portal del cliente si tiene cuenta activa. In-app no se filtra
            // por ContactPolicy (es otro canal). Publicaciones viejas sin CustomerId no entran acá.
            if (evt.CustomerId is { } customerId)
            {
                var portalUserId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
                    evt.TenantId,
                    customerId,
                    recipientResolver,
                    ct
                );
                if (portalUserId is { } userId)
                {
                    var title = $"Factura {evt.InvoiceNumber} enviada";
                    await dispatcher.RecordInAppAsync(
                        evt.TenantId,
                        $"user:{userId:N}",
                        title,
                        NotificationCategory.Billing,
                        "billing.invoice_sent_to_customer",
                        evt.EventId,
                        correlation.CorrelationId,
                        recipientUserId: userId,
                        inApp: new InAppNotificationRequest(
                            NotificationKinds.BillingInvoiceSentToCustomer,
                            userId,
                            evt.EventId,
                            EventKey,
                            evt.OccurredOn,
                            title,
                            FallbackBody: $"Tu oficina te envió la factura {evt.InvoiceNumber}.",
                            ResourceId: evt.InvoiceId.ToString(),
                            ResourceContext: new Dictionary<string, string>
                            {
                                ["customerId"] = customerId.ToString(),
                                ["invoiceId"] = evt.InvoiceId.ToString(),
                            },
                            Params: new Dictionary<string, string> { ["invoiceNumber"] = evt.InvoiceNumber }
                        ),
                        ct: ct
                    );
                }
            }
        }
    }

    /// <summary>Centavos a texto. La plantilla recibe el importe ya formateado, no lo compone.</summary>
    private static string FormatAmount(long cents, string currency) =>
        $"{cents / 100m:N2} {currency.ToUpperInvariant()}";
}
