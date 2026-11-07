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
        SessionAccessCutoff accessCutoff,
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
            // TEN-4: suspender y reactivar pueden llegar al revés.
            var tenant = await tenants.GetByIdAsync(evt.ChangedTenantId, ct);
            if (tenant is not null && !tenant.AcceptStatusVersion(evt.TenantVersion))
                return;

            await tenants.SetActiveAsync(evt.ChangedTenantId, evt.IsActive, ct);

            // Tenant suspendido/cerrado: se cortan todas las sesiones activas. A5 (G10) — antes esto
            // revocaba SOLO en la base, así que el access token de cada usuario seguía siendo válido
            // hasta 15 minutos después de suspender el tenant. Ahora denylistea el sid y lo anuncia.
            if (!evt.IsActive)
            {
                await accessCutoff.ForTenantAsync(evt.ChangedTenantId, "tenant_suspended", ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
