using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina guardó su perfil: la proyección local copia el nombre comercial que ve el cliente en el
/// checkout. Ordenado por <c>Version</c> (igual o menor se ignora: idempotente). Sin fila aún (llegó antes que
/// <c>TenantCreated</c>) se reintenta: el perfil se aplica cuando la fila exista.
/// </summary>
public static class TenantProfileChangedConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
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
            var tenant = await tenants.GetByIdAsync(evt.ProfileTenantId, ct);
            if (tenant is null)
                throw new DependencyNotReadyException("Tenant", $"profile v{evt.Version} of {evt.ProfileTenantId}");

            if (tenant.ApplyProfile(evt.Version, evt.DisplayName, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
