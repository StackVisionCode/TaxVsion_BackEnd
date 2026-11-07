using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>
/// La oficina renombró su subdominio primario: el directorio de Scribe guarda el slug nuevo. Sin fila aún
/// (llegó antes que <c>TenantCreated</c> o el backfill) se reintenta, como el perfil (BB-7). Idempotente.
/// </summary>
public static class TenantSubdomainChangedConsumer
{
    public static async Task Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProfileRef> logger,
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
            var existing =
                await repository.GetByTenantIdAsync(evt.TenantId, ct)
                ?? throw new DependencyNotReadyException("Tenant", $"subdomain change of {evt.TenantId}");
            if (!existing.ApplySubDomain(evt.NewSlug, evt.OccurredOn, DateTime.UtcNow))
                return;

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Tenant {TenantId} subdomain renamed in the Scribe directory.", evt.TenantId);
        }
    }
}
