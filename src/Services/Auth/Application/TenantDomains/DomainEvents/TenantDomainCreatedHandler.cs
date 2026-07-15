using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.DomainEvents;

/// <summary>
/// Único lugar que reacciona a "se creó un TenantDomain" — sin importar si lo disparó
/// un admin (CreateTenantDomainHandler), el alta automática de un tenant nuevo
/// (TenantCreatedConsumer) o el backfill de tenants viejos (TenantDomainBackfillService).
/// AuthDbContext.SaveChangesAsync lo despacha in-process antes de confirmar.
/// </summary>
public static class TenantDomainCreatedHandler
{
    public static async Task Handle(
        TenantDomainCreated evt,
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
                AuthAuditAction.TenantDomainCreated,
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
            new TenantDomainCreatedIntegrationEvent
            {
                TenantId = evt.TenantId,
                DomainId = evt.DomainId,
                Host = evt.Host,
                DomainType = evt.DomainType,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
