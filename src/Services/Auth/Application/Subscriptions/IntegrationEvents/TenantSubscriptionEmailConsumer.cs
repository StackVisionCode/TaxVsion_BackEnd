using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Notificaciones de ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3). Auth es el
/// dueño del contacto: consume el evento de estado (solo TenantId), resuelve el email del admin/owner y
/// publica <see cref="TenantSubscriptionEmailRequestedIntegrationEvent"/> para que Notification lo envíe.
/// Solo dispara en los estados con copy relevante: Suspended,
/// Expired y la vuelta a Active por recuperación/reactivación. El resto (PastDue transitorio, Trialing,
/// Cancelled, Active inicial) no manda email.
/// </summary>
public static class TenantSubscriptionEmailConsumer
{
    private static readonly HashSet<string> RecoveryReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SubscriptionChangeReason.PaymentRecovered),
        nameof(SubscriptionChangeReason.AdminReactivated),
        nameof(SubscriptionChangeReason.SelfServiceRenewed),
    };

    /// <summary>Motivos que viajan por el canal de email pero no son transiciones de estado.</summary>
    private static readonly HashSet<string> NonTransitionNotifyReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SubscriptionChangeReason.RecurringChargeSucceeded),
        nameof(SubscriptionChangeReason.RecurringChargeFailed),
        nameof(SubscriptionChangeReason.BillingModeChanged),
        nameof(SubscriptionChangeReason.MandateCreated),
    };

    public static async Task Handle(
        TenantSubscriptionStatusChangedIntegrationEvent evt,
        IUserRepository users,
        ITenantRegistry tenants,
        IMessageBus bus,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        ILogger<TenantSubscriptionEmailRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            if (!ShouldNotify(evt.Status, evt.Reason))
                return;

            var admin = await users.GetPrimaryAdminAsync(evt.TenantId, ct);
            if (admin is null)
            {
                logger.LogWarning(
                    "No primary admin to notify for tenant {TenantId} on subscription {Status}.",
                    evt.TenantId,
                    evt.Status
                );
                return;
            }

            var tenant = await tenants.GetByIdAsync(evt.TenantId, ct);
            var settings = platform.Current;
            var renewUrl =
                tenant is not null && !string.IsNullOrWhiteSpace(tenant.SubDomain)
                    ? $"https://{settings.TenantHost(tenant.SubDomain)}"
                    : settings.Website;

            bus.TenantId = PlatformTenant.Id.ToString();
            await bus.PublishAsync(
                new TenantSubscriptionEmailRequestedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    Email = admin.Email,
                    FirstName = admin.Name,
                    Status = evt.Status,
                    Reason = evt.Reason,
                    AccessEndsAtUtc = evt.AccessEndsAtUtc,
                    FailureCode = evt.FailureCode,
                    RenewUrl = renewUrl,
                    CorrelationId = correlationId,
                    // Reenviar los campos de cobro recurrente/mandate/preferencia del evento de estado.
                    Amount = evt.Amount,
                    CardLast4 = evt.CardLast4,
                    CardBrand = evt.CardBrand,
                    ReceiptUrl = evt.ReceiptUrl,
                    ManageUrl = $"{renewUrl.TrimEnd('/')}/account/manage-subscription",
                    NewMode = evt.NewMode,
                    Cadence = evt.Cadence,
                    DateCharged = evt.DateCharged,
                    NextChargeDate = evt.NextChargeDate,
                }
            );

            logger.LogInformation(
                "Queued subscription {Status} email for tenant {TenantId} admin.",
                evt.Status,
                evt.TenantId
            );
        }
    }

    /// <summary>Cancelar al fin del período no cambia el estado (sigue Active), así que el aviso se reconoce
    /// por el motivo, no por el estado.</summary>
    private static readonly HashSet<string> ScheduledCancellationReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SubscriptionChangeReason.CancellationScheduled),
        nameof(SubscriptionChangeReason.AccessEnding),
    };

    private static bool IsScheduledCancellation(string status, string reason) =>
        (
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Trialing", StringComparison.OrdinalIgnoreCase)
        ) && ScheduledCancellationReasons.Contains(reason);

    private static bool ShouldNotify(string status, string reason) =>
        // A6 — un re-anuncio del job de anti-entropía no es una transición y no se le avisa a nadie:
        // si no, un tenant vencido recibiría el correo de "tu suscripción venció" CADA día que corra
        // el job. Va primero porque `Expired`/`Suspended` notifican sin mirar el motivo.
        !string.Equals(reason, nameof(SubscriptionChangeReason.Reconciliation), StringComparison.OrdinalIgnoreCase)
        && (
            IsScheduledCancellation(status, reason)
            || NonTransitionNotifyReasons.Contains(reason)
            || string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) && RecoveryReasons.Contains(reason))
        );
}
