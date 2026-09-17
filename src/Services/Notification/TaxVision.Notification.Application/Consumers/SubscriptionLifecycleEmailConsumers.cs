using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Envía los emails de ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3): pago
/// fallido/gracia, suspendido, expirado y reactivado. Auth ya resolvió el destinatario y publicó
/// <see cref="TenantSubscriptionEmailRequestedIntegrationEvent"/>; acá solo se elige el template por
/// <c>Status</c>, se renderiza en Scribe y se despacha. Molde: <c>OnboardingPaymentFailedNotificationConsumer</c>.
/// </summary>
public static class SubscriptionLifecycleEmailConsumer
{
    public static async Task Handle(
        TenantSubscriptionEmailRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ISubscriptionEmailMetrics metrics,
        CancellationToken ct
    )
    {
        var (eventKey, templateKey) = ResolveTemplate(evt.Status);
        if (eventKey is null || templateKey is null)
            return;

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var render = (
                await scribeClient.RenderAsync(
                    eventKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["first_name"] = evt.FirstName,
                        ["plan_name"] = evt.PlanName ?? "tu plan",
                        ["renew_url"] = evt.RenewUrl,
                        ["grace_end_date"] = FormatDate(evt.GracePeriodEndsAtUtc),
                        ["failure_reason"] = evt.FailureCode ?? string.Empty,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered(eventKey);

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: templateKey,
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );

            metrics.RecordDunningEmailSent(templateKey);
        }
    }

    private static (string? EventKey, string? TemplateKey) ResolveTemplate(string status) =>
        status.ToLowerInvariant() switch
        {
            "graceperiod" => ("subscription.payment_failed.v1", "subscription.payment_failed"),
            "suspended" => ("subscription.suspended.v1", "subscription.suspended"),
            "expired" => ("subscription.expired.v1", "subscription.expired"),
            "active" => ("subscription.reactivated.v1", "subscription.reactivated"),
            _ => (null, null),
        };

    private static string FormatDate(DateTime? value) =>
        value is { } date ? date.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-ES")) : string.Empty;
}
