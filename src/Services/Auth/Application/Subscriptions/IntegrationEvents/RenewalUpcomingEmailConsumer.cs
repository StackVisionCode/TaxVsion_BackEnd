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
/// Recordatorios pre-vencimiento (7 y 1 día). Auth resuelve el admin del tenant y pide a Notification que
/// renderice y envíe. Reusa el canal de ciclo de vida (mismo destinatario, mismo flujo) con un motivo
/// nuevo <see cref="SubscriptionChangeReason.RenewalUpcoming"/>.
/// </summary>
public static class RenewalUpcomingEmailConsumer
{
    public static async Task Handle(
        SubscriptionRenewalUpcomingIntegrationEvent evt,
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
            var admin = await users.GetPrimaryAdminAsync(evt.TenantId, ct);
            if (admin is null)
            {
                logger.LogWarning("No primary admin to warn about renewal for tenant {TenantId}.", evt.TenantId);
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
                    Status = "Active",
                    Reason = nameof(SubscriptionChangeReason.RenewalUpcoming),
                    PlanName = evt.PlanCode,
                    DaysUntilRenewal = evt.DaysUntilDue,
                    NextRenewalAtUtc = evt.DueAtUtc,
                    RenewUrl = renewUrl,
                    CorrelationId = correlationId,
                }
            );

            logger.LogInformation(
                "Queued renewal-upcoming email ({Days}d) for tenant {TenantId} admin.",
                evt.DaysUntilDue,
                evt.TenantId
            );
        }
    }
}
