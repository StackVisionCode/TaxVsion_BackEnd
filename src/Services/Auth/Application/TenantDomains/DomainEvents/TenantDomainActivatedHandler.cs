using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.DomainEvents;

/// <summary>
/// Único lugar que reacciona a "un TenantDomain pasó a Active" — cubre tanto la
/// activación manual (ActivateTenantDomainHandler) como la automática
/// (TenantDomainProvisioningPoller), sin duplicar la auditoría ni la publicación.
/// </summary>
public static class TenantDomainActivatedHandler
{
    public static async Task Handle(
        TenantDomainActivated evt,
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
                AuthAuditAction.TenantDomainActivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantDomain",
                targetId: evt.DomainId
            ),
            ct
        );

        // Verified + Activated: Cloudflare confirmó la validación DNS/TLS y, en el
        // mismo paso, el estado propio pasó a Active (ver ICloudflareProvisioningClient, ACL).
        await bus.PublishAsync(
            new TenantDomainVerifiedIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                Host = evt.Host,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await bus.PublishAsync(
            new TenantDomainActivatedIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                Host = evt.Host,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
