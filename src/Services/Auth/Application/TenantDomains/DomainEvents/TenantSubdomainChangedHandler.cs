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
        ITenantRegistry tenants,
        ITenantResolutionCache resolutionCache,
        CancellationToken ct
    )
    {
        // La réplica local del tenant guarda el slug (lo usan el link de renovación y las invitaciones):
        // si cambió el primario, cambia acá en la misma transacción que la auditoría y el evento.
        if (evt.IsPrimary && await tenants.GetByIdAsync(evt.TenantId, ct) is { } tenant)
            tenant.ChangeSubDomain(evt.NewSlug);

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
                OldSlug = evt.OldSlug,
                NewSlug = evt.NewSlug,
                IsPrimary = evt.IsPrimary,
                CorrelationId = correlation.CorrelationId,
            }
        );

        // El host viejo deja de resolver ya (además del que invalida el comando, por si el rename vino
        // de otro camino).
        await resolutionCache.InvalidateAsync(evt.OldHost, ct);
    }
}
