using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.DomainEvents;

/// <summary>Único lugar que reacciona a "Cloudflare bloqueó/rechazó un custom hostname en Provisioning" (hoy solo lo dispara el poller).</summary>
public static class TenantDomainProvisioningFailedHandler
{
    public static async Task Handle(
        TenantDomainProvisioningFailed evt,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        await audit.AddAsync(
            AuthAuditLog.Record(
                evt.TenantId,
                evt.ActingUserId,
                AuthAuditAction.TenantDomainProvisioningFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantDomain",
                targetId: evt.DomainId,
                detailsJson: $$"""{"reason":"{{evt.Reason}}"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantDomainProvisioningFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                Host = evt.Host,
                Reason = evt.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
