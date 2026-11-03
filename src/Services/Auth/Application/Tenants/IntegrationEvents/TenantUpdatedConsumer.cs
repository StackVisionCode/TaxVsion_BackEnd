using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina cambió de nombre o de zona horaria: la proyección local los copia (alimenta
/// <c>/auth/me</c> y la zona por defecto de los usuarios sin zona propia). Idempotente y ordenado por
/// <c>UpdatedAtUtc</c>: un evento más viejo que lo aplicado se ignora. Sin fila no hay nada que
/// actualizar (la crea <c>TenantCreated</c>, que no pisa un renombre ya aplicado).
/// </summary>
public static class TenantUpdatedConsumer
{
    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantUpdatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var tenant = await tenants.GetByIdAsync(evt.UpdatedTenantId, ct);
            if (tenant is null)
            {
                logger.LogInformation(
                    "Tenant {TenantId} is not projected in Auth; update skipped.",
                    evt.UpdatedTenantId
                );
                return;
            }

            if (!tenant.ApplyTenantUpdate(evt.Name, evt.DefaultTimeZoneId, evt.UpdatedAtUtc))
            {
                logger.LogInformation(
                    "Tenant {TenantId} update from {UpdatedAtUtc:o} ignored (stale or invalid).",
                    evt.UpdatedTenantId,
                    evt.UpdatedAtUtc
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
