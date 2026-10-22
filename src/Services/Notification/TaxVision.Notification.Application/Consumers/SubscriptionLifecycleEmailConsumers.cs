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
                        ["plan_name"] = evt.PlanName ?? "your plan",
                        ["renew_url"] = evt.RenewUrl,
                        ["manage_url"] = evt.ManageUrl ?? evt.RenewUrl,
                        ["access_ends_date"] = FormatDate(evt.AccessEndsAtUtc),
                        ["days_until_end"] = DaysUntil(evt.AccessEndsAtUtc),
                        ["days_until_renewal"] = evt.DaysUntilRenewal?.ToString(CultureInfo.InvariantCulture) ?? "",
                        ["renewal_date"] = FormatDate(evt.NextRenewalAtUtc),
                        ["failure_reason"] = evt.FailureCode ?? string.Empty,
                        // Variables para cobro recurrente, mandate y cambio de preferencia.
                        ["amount"] = evt.Amount ?? string.Empty,
                        ["card_brand"] = CardBrandNames.Display(evt.CardBrand),
                        ["card_last4"] = evt.CardLast4 ?? "----",
                        ["receipt_url"] = evt.ReceiptUrl ?? string.Empty,
                        ["new_mode"] = evt.NewMode ?? string.Empty,
                        ["cadence"] = evt.Cadence ?? "month",
                        ["date_charged"] = evt.DateCharged ?? string.Empty,
                        ["next_charge_date"] = evt.NextChargeDate ?? string.Empty,
                        // Cobro automático de renovación fallido: uno por intento.
                        ["item_description"] = evt.ItemDescription ?? evt.PlanName ?? "your subscription",
                        ["has_card"] = !string.IsNullOrWhiteSpace(evt.CardLast4),
                        ["attempt_number"] = evt.AttemptNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["max_attempts"] = evt.MaxAttempts?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["is_final"] = evt.IsFinalAttempt,
                        ["next_retry_date"] = FormatDate(evt.NextAttemptAtUtc),
                        ["friendly_reason"] = ChargeFailureReasons.Describe(evt.FailureCode),
                        ["suspension_effect"] = ChargeFailureConsequences.Describe(
                            evt.Consequence,
                            evt.ItemDescription
                        ),
                    },
                    ct
                )
            ).EnsureRendered(eventKey);

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: templateKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ) with
                {
                    // El recordatorio lo publica un job: cada pasada trae un EventId nuevo, así que la
                    // deduplicación por evento no sirve. La clave se ancla al tenant y a la fecha de fin.
                    IdempotencyKey = templateKey switch
                    {
                        "subscription.access_ending" =>
                            $"{evt.TenantId:N}:access-ending:{evt.AccessEndsAtUtc:yyyyMMdd}",
                        "subscription.renewal_upcoming" =>
                            $"{evt.TenantId:N}:renewal-upcoming:{evt.NextRenewalAtUtc:yyyyMMdd}:{evt.DaysUntilRenewal}",
                        // Un correo por intento: un reenvío del mismo aviso no lo repite.
                        "subscription.recurring_charge_failed" when evt.SaaSPaymentId is { } paymentId =>
                            $"{paymentId:N}:attempt-{evt.AttemptNumber}:{evt.Email.Trim().ToLowerInvariant()}",
                        _ => null,
                    },
                },
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

        // Cobro automático de renovación fallido (plan, asiento o add-on): uno por intento. Va antes del
        // estado porque el último intento llega con la suscripción ya suspendida y ese correo lo explica.
        if (string.Equals(reason, "RenewalChargeFailed", StringComparison.OrdinalIgnoreCase))
            return ("subscription.recurring_charge_failed.v1", "subscription.recurring_charge_failed");

        // Recordatorio pre-vencimiento (7 o 1 día). Lo publica RenewalNotificationJob y Auth lo re-emite.
        if (string.Equals(reason, "RenewalUpcoming", StringComparison.OrdinalIgnoreCase))
            return ("subscription.renewal_upcoming.v1", "subscription.renewal_upcoming");

        // Cobros recurrentes y cambios de preferencia no son transiciones de estado:
        // viajan por el mismo canal para reusar la resolución de destinatario y el envío.
        if (string.Equals(reason, "RecurringChargeSucceeded", StringComparison.OrdinalIgnoreCase))
            return ("subscription.recurring_charge.v1", "subscription.recurring_charge");
        if (string.Equals(reason, "RecurringChargeFailed", StringComparison.OrdinalIgnoreCase))
            return ("subscription.recurring_charge_failed.v1", "subscription.recurring_charge_failed");
        if (string.Equals(reason, "BillingModeChanged", StringComparison.OrdinalIgnoreCase))
            return ("subscription.billing_mode_changed.v1", "subscription.billing_mode_changed");
        if (string.Equals(reason, "MandateCreated", StringComparison.OrdinalIgnoreCase))
            return ("subscription.mandate_created.v1", "subscription.mandate_created");

        return status.ToLowerInvariant() switch
        {
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
