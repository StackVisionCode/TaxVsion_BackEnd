using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;

/// <summary>
/// La oficina guardó su perfil: la proyección copia lo que va en el "Billed to" del recibo SaaS (nombre
/// legal, EIN y dirección postal en una línea). Ordenado por <c>Version</c> e idempotente. Sin fila se
/// ignora: la crea <c>TenantCreated</c> y el perfil vuelve a llegar con el republish.
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
                    "TenantProfileChanged v{Version} for unknown tenant {TenantId}; ignoring.",
                    evt.Version,
                    evt.ProfileTenantId
                );
                return;
            }

            var address = evt.ResolvePostalAddress()?.ToSingleLine();
            if (tenant.ApplyProfile(evt.Version, evt.LegalName, evt.Ein, address, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
