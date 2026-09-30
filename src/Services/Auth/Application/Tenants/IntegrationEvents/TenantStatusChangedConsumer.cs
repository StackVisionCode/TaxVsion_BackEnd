using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantStatusChangedConsumer
{
    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocations,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            await tenants.SetActiveAsync(evt.ChangedTenantId, evt.IsActive, ct);

            // Tenant suspendido/cerrado: se cortan todas las sesiones activas. A5 (G10) — antes esto
            // revocaba SOLO en la base, así que el access token de cada usuario seguía siendo válido
            // hasta 15 minutos después de suspender el tenant. Ahora denylistea el sid y lo anuncia.
            if (!evt.IsActive)
            {
                await SessionAccessCutoff.ForTenantAsync(
                    evt.ChangedTenantId,
                    "tenant_suspended",
                    sessions,
                    denylist,
                    revocations,
                    ct
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
