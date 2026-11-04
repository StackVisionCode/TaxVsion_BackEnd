using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina guardó su perfil: la proyección local copia el nombre comercial que ve el cliente en el
/// checkout. Ordenado por <c>Version</c> (igual o menor se ignora: idempotente). Sin fila se ignora: la
/// crea <c>TenantCreated</c> y el perfil vuelve a llegar con el republish de Tenant.
/// </summary>
public static class TenantProfileChangedConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<Tenant> logger,
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
            {
                logger.LogInformation(
                    "TenantProfileChanged v{Version} ignored: tenant {TenantId} is not projected yet.",
                    evt.Version,
                    evt.ProfileTenantId
                );
                return;
            }

            if (tenant.ApplyProfile(evt.Version, evt.DisplayName, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
