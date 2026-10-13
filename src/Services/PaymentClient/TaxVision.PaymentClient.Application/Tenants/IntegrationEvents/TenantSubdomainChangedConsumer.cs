using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina renombró su subdominio primario: la proyección local guarda el slug nuevo, que es con el
/// que se arman los hosts públicos de la oficina. Idempotente: si ya es el nuevo, no hace nada.
/// </summary>
public static class TenantSubdomainChangedConsumer
{
    public static async Task Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (!evt.IsPrimary)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var tenant = await tenants.GetByIdAsync(evt.TenantId, ct);
            if (tenant is null || tenant.SubDomain == evt.NewSlug)
                return;

            tenant.ChangeSubDomain(evt.NewSlug, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
