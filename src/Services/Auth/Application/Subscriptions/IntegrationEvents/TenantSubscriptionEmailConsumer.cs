using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Notificaciones de ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3). Auth es el
/// dueño del contacto: consume el evento de estado (solo TenantId), resuelve el email del admin/owner y
/// publica <see cref="TenantSubscriptionEmailRequestedIntegrationEvent"/> para que Notification lo envíe.
/// Solo dispara en los estados con copy relevante: GracePeriod (pago falló, aviso de gracia), Suspended,
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

    public static async Task Handle(
        TenantSubscriptionStatusChangedIntegrationEvent evt,
        IUserRepository users,
        ITenantRegistry tenants,
        IMessageBus bus,
        IOptions<TenantDomainOptions> domainOptions,
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
            var baseDomain = domainOptions.Value.BaseDomain.Trim().Trim('.');
            var renewUrl =
                tenant is not null && !string.IsNullOrWhiteSpace(tenant.SubDomain)
                    ? $"https://{tenant.SubDomain}.{baseDomain}"
                    : $"https://{baseDomain}";

            bus.TenantId = PlatformTenant.Id.ToString();
            await bus.PublishAsync(
                new TenantSubscriptionEmailRequestedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    Email = admin.Email,
                    FirstName = admin.Name,
                    Status = evt.Status,
                    Reason = evt.Reason,
                    GracePeriodEndsAtUtc = evt.GracePeriodEndsAtUtc,
                    FailureCode = evt.FailureCode,
                    RenewUrl = renewUrl,
                    CorrelationId = correlationId,
                }
            );

            logger.LogInformation(
                "Queued subscription {Status} email for tenant {TenantId} admin.",
                evt.Status,
                evt.TenantId
            );
        }
    }

    private static bool ShouldNotify(string status, string reason) =>
        string.Equals(status, "GracePeriod", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) && RecoveryReasons.Contains(reason));
}
