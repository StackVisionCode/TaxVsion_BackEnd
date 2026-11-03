using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina cambió de nombre o de zona horaria: la proyección local los copia. Ordenado por
/// <c>UpdatedAtUtc</c> (un evento más viejo que lo aplicado se ignora) e idempotente. Sin fila no hay
/// nada que actualizar: la crea <c>TenantCreated</c>, que nunca pisa una fila existente.
/// </summary>
public static class TenantUpdatedConsumer
{
    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
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
                return;

            if (tenant.ApplyTenantUpdate(evt.Name, evt.DefaultTimeZoneId, evt.UpdatedAtUtc, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
