using System.Diagnostics;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;

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
        IScribeRenderClient scribeClient,
        ICustomerContactPolicyRepository contactPolicies,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<InvoiceSentLog> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // Diagnóstico de latencia: cuánto pasó desde que Billing publicó (cola + otros eventos) y cuánto tarda cada paso propio.
            var consumedLag = DateTime.UtcNow - evt.OccurredOn;
            var timer = Stopwatch.StartNew();

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
                // Sin cliente (publicaciones viejas) no hay idioma que buscar: inglés.
                var locale = evt.CustomerId is { } recipientId
                    ? await locales.ForCustomerAsync(evt.TenantId, recipientId, ct)
                    : locales.FromEventLanguage(null);
                var render = (
                    await scribeClient.RenderForRecipientAsync(
                        EventKey,
                        evt.TenantId,
                        new Dictionary<string, object?>
                        {
                            ["invoice_number"] = evt.InvoiceNumber,
                            ["customer_name"] = evt.CustomerName,
                            ["amount_due"] = FormatAmount(evt.AmountDueCents, evt.Currency),
                            // Con la fecha congelada (P6) va la civil; una factura vieja manda el instante.
                            ["due_date"] = evt.DueOn is { } dueOn
                                ? ScribeDates.Date(dueOn)
                                : ScribeDates.Instant(evt.DueDateUtc),
                            ["payment_link"] = evt.PaymentLink,
                            ["has_pdf"] = evt.PdfFileId is not null,
                        },
                        ScribeRenderAudience.Client,
                        locale,
                        ct
                    )
                ).EnsureRendered(EventKey);
                var renderedMs = timer.ElapsedMilliseconds;

                // Correo al cliente (sin cuenta del staff): obligatorio por catálogo, sin preferencia.
                await dispatcher.DispatchAsync(
                    Intent(
                        evt,
                        correlation,
                        new NotificationTarget(null, evt.CustomerEmail, CustomerId: evt.CustomerId, Locale: locale)
                    ) with
                    {
                        Email = new EmailContent(
                            EventKey,
                            new Dictionary<string, object?>(),
                            ScribeRenderAudience.Client,
                            TemplateKey: TemplateKey,
                            Render: (_, _) => Task.FromResult(render),
                            AttachmentFileIds: evt.PdfFileId is { } pdf ? [pdf] : null
                        ),
                    },
                    ct
                );

                logger.LogInformation(
                    "Invoice email {InvoiceNumber} handed to Postmaster: Billing→Notification lag {LagMs} ms, Scribe render {RenderMs} ms, total in Notification {TotalMs} ms.",
                    evt.InvoiceNumber,
                    (long)consumedLag.TotalMilliseconds,
                    renderedMs,
                    timer.ElapsedMilliseconds
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
                    var parameters = new Dictionary<string, string> { ["invoiceNumber"] = evt.InvoiceNumber };
                    await dispatcher.DispatchAsync(
                        Intent(evt, correlation, new NotificationTarget(userId)) with
                        {
                            InApp = new InAppContent(
                                Params: parameters,
                                ResourceId: evt.InvoiceId.ToString(),
                                ResourceContext: new Dictionary<string, string>
                                {
                                    ["customerId"] = customerId.ToString(),
                                    ["invoiceId"] = evt.InvoiceId.ToString(),
                                }
                            ),
                        },
                        ct
                    );
                }
            }
        }
    }

    private static NotificationIntent Intent(
        InvoiceSentToCustomerIntegrationEvent evt,
        ICorrelationContext correlation,
        NotificationTarget target
    ) =>
        new(
            NotificationKinds.BillingInvoiceSentToCustomer,
            evt.TenantId,
            target,
            evt.EventId,
            EventKey,
            evt.OccurredOn,
            correlation.CorrelationId
        );

    /// <summary>Centavos a texto. La plantilla recibe el importe ya formateado, no lo compone.</summary>
    private static string FormatAmount(long cents, string currency) =>
        $"{cents / 100m:N2} {currency.ToUpperInvariant()}";
}

/// <summary>Ancla de categoría para el <see cref="ILogger{T}"/> del consumer (es estático: no puede ser el tipo del logger).</summary>
public sealed class InvoiceSentLog { }
