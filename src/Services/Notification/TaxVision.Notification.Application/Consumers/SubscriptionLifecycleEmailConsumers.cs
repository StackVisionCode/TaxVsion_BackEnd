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
        var (eventKey, templateKey) = ResolveTemplate(evt.Status, evt.Reason);
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
                        ["access_ends_date"] = FormatDate(evt.AccessEndsAtUtc),
                        ["days_until_end"] = DaysUntil(evt.AccessEndsAtUtc),
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
                    // El recordatorio lo publica un job: cada pasada trae un EventId nuevo, así que la
                    // deduplicación por evento no sirve. La clave se ancla al tenant y a la fecha de fin.
                    IdempotencyKey: templateKey == "subscription.access_ending"
                        ? $"{evt.TenantId:N}:access-ending:{evt.AccessEndsAtUtc:yyyyMMdd}"
                        : null,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );

            metrics.RecordDunningEmailSent(templateKey);
        }
    }

    private static (string? EventKey, string? TemplateKey) ResolveTemplate(string status, string reason)
    {
        // Cancelar al fin del período deja la suscripción Active: el aviso se reconoce por el motivo.
        if (
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            && string.Equals(reason, "CancellationScheduled", StringComparison.OrdinalIgnoreCase)
        )
            return ("subscription.cancellation_scheduled.v1", "subscription.cancellation_scheduled");

        // Recordatorio de que se acerca el fin: lo publica un job, no una transición.
        if (
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            && string.Equals(reason, "AccessEnding", StringComparison.OrdinalIgnoreCase)
        )
            return ("subscription.access_ending.v1", "subscription.access_ending");

        return status.ToLowerInvariant() switch
        {
            "graceperiod" => ("subscription.payment_failed.v1", "subscription.payment_failed"),
            "suspended" => ("subscription.suspended.v1", "subscription.suspended"),
            "expired" => ("subscription.expired.v1", "subscription.expired"),
            "active" => ("subscription.reactivated.v1", "subscription.reactivated"),
            _ => (null, null),
        };
    }

    // El copy de estas plantillas es inglés (los eventos no traen idioma del destinatario), así que la fecha
    // también: antes salía en español dentro de un texto en inglés.
    private static string DaysUntil(DateTime? value) =>
        value is { } date
            ? Math.Max(0, (date.Date - DateTime.UtcNow.Date).Days).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private static string FormatDate(DateTime? value) =>
        value is { } date ? date.ToString("MMMM d, yyyy", new CultureInfo("en-US")) : string.Empty;
}
