using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.DomainEvents;

/// <summary>Único lugar que reacciona a "se deshabilitó un TenantDomain".</summary>
public static class TenantDomainDisabledHandler
{
    public static async Task Handle(
        TenantDomainDisabled evt,
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
                AuthAuditAction.TenantDomainDisabled,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantDomain",
                targetId: evt.DomainId
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantDomainDisabledIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                Host = evt.Host,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
