using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.TenantEvents;

/// <summary>
/// La oficina renombró su subdominio primario: el directorio de Postmaster guarda el slug nuevo. Sin
/// fila todavía no hay nada que renombrar (la crea TenantCreated o el backfill). Idempotente.
/// </summary>
public static class TenantSubdomainChangedConsumer
{
    public static async Task Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantDirectoryEntry> logger,
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
            var existing = await repository.FindAsync(evt.TenantId, ct);
            if (existing is null || existing.SubDomain == evt.NewSlug)
                return;

            existing.Rename(existing.Name, evt.NewSlug, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Tenant {TenantId} subdomain renamed in the Postmaster directory.", evt.TenantId);
        }
    }
}
