using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.TenantEvents;

/// <summary>
/// La oficina guardó su perfil: el From de sus correos pasa a llevar el nombre comercial si lo tiene.
/// Postmaster solo guarda ese nombre. Ordenado por <c>Version</c> (una igual o menor se ignora) e
/// idempotente. Sin fila se ignora: la crea <c>TenantCreated</c> o el backfill, y el perfil vuelve a
/// llegar al republicarlo.
/// </summary>
public static class TenantProfileChangedConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await repository.FindAsync(evt.ProfileTenantId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "Tenant {TenantId} is not in the Postmaster directory yet; profile skipped.",
                    evt.ProfileTenantId
                );
                return;
            }

            if (!existing.ApplyProfile(evt.Version, evt.DisplayName, DateTime.UtcNow))
            {
                logger.LogInformation(
                    "Tenant {TenantId} profile version {Version} ignored (stale).",
                    evt.ProfileTenantId,
                    evt.Version
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Tenant {TenantId} profile version {Version} applied in the Postmaster directory.",
                evt.ProfileTenantId,
                evt.Version
            );
        }
    }
}
