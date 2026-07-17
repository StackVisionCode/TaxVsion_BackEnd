using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.DomainEvents;

/// <summary>Único lugar que reacciona a "se renombró el subdominio primario de un tenant" (Fase A7).</summary>
public static class TenantSubdomainChangedHandler
{
    public static async Task Handle(
        TenantSubdomainChanged evt,
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
                AuthAuditAction.TenantSubdomainChanged,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantDomain",
                targetId: evt.DomainId,
                detailsJson: $$"""{"oldHost":"{{evt.OldHost}}","newHost":"{{evt.NewHost}}"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantSubdomainChangedIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                OldHost = evt.OldHost,
                NewHost = evt.NewHost,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
