using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina guardó su perfil: la proyección copia lo que va en el "Billed to" del recibo SaaS (nombre
/// legal, EIN y dirección postal en una línea). Ordenado por <c>Version</c> e idempotente. Sin fila aún (llegó
/// antes que <c>TenantCreated</c>) se reintenta: el perfil se aplica cuando la fila exista.
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

            var address = evt.ResolvePostalAddress()?.ToSingleLine();
            if (tenant.ApplyProfile(evt.Version, evt.LegalName, evt.Ein, address, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
