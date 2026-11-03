using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>
/// La oficina se renombró: los correos que firma pasan a llevar el nombre nuevo en la cabecera y el
/// pie. Scribe no guarda la zona horaria. Ordenado por <c>UpdatedAtUtc</c> (un evento más viejo se
/// ignora) e idempotente. Sin fila no hay nada que renombrar: la crea <c>TenantCreated</c> o el
/// backfill, que lee el nombre vigente del directorio de Tenant.
/// </summary>
public static class TenantUpdatedConsumer
{
    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProfileRef> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await repository.GetByTenantIdAsync(evt.UpdatedTenantId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "Tenant {TenantId} is not in the Scribe directory yet; rename skipped.",
                    evt.UpdatedTenantId
                );
                return;
            }

            if (!existing.ApplyTenantUpdate(evt.Name, evt.UpdatedAtUtc, DateTime.UtcNow))
            {
                logger.LogInformation(
                    "Tenant {TenantId} rename from {UpdatedAtUtc:o} ignored (stale).",
                    evt.UpdatedTenantId,
                    evt.UpdatedAtUtc
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Tenant {TenantId} renamed in the Scribe directory.", evt.UpdatedTenantId);
        }
    }
}
