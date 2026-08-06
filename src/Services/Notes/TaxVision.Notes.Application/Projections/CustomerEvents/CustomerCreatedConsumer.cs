using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

// ---------------------------------------------------------------------------
// Fase 4B — mantiene CustomerDirectoryEntry, la proyección local que Notes usa para validar
// (SOFT — solo existencia/visibilidad, nunca ownership de negocio) que un CustomerId referenciado
// por una nota realmente pertenece al tenant. Mismo criterio reactivo que Correspondence
// (Fase 2): como no existe ningún endpoint M2M de enumeración de tenants en el monorepo, la
// primera vez que Notes "ve" un tenant es al recibir cualquier evento de Customer — por eso
// EnsureBackfilledAsync es siempre la primera línea de todo handler de este grupo.
// ---------------------------------------------------------------------------

public static class CustomerCreatedConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is not null)
            {
                // El backfill (paginado en paralelo con este evento) ya pudo haber creado la fila.
                existing.ApplyIfNewer(evt.DisplayName, CustomerDirectoryStatus.Active, evt.OccurredOn);
            }
            else
            {
                var entry = CustomerDirectoryEntry.Create(
                    evt.TenantId,
                    evt.CustomerId,
                    evt.DisplayName,
                    CustomerDirectoryStatus.Active,
                    evt.OccurredOn
                );
                await repository.AddAsync(entry, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry upserted for tenant {TenantId}, customer {CustomerId} (Created).",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }
}
