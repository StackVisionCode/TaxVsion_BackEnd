using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Bloque guardado del import ⇒ cada ficha se aplica como el alta (nueva) o la edición (actualizada) del cliente,
/// con la hora del bloque para el orden. Así un firmante importado se reconoce por email (P-14) sin esperar a la
/// reconciliación. Un cliente no activo deja de ser coincidencia, como con Deactivated/Archived.
/// </summary>
public static class CustomersImportChunkCommittedConsumer
{
    public static async Task Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerEmailProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailProjection> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            // Dos filas pueden tocar al mismo cliente: vale la última, y la fila agregada sin guardar no se duplica.
            foreach (var card in evt.Customers.GroupBy(card => card.CustomerId).Select(group => group.Last()))
                await ApplyAsync(evt, card, repository, ct);

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Import chunk {ChunkNumber} of job {ImportJobId} projected for tenant {TenantId} ({Count} clients).",
                evt.ChunkNumber,
                evt.ImportJobId,
                evt.TenantId,
                evt.Customers.Count
            );
        }
    }

    private static async Task ApplyAsync(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ImportedCustomerCard card,
        ICustomerEmailProjectionRepository repository,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, card.CustomerId, ct);
        if (!string.Equals(card.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            existing?.MarkArchived(evt.OccurredOn);
            return;
        }

        // Sin email no hay nada que proyectar; nunca se borra el conocido.
        var normalizedEmail = card.PrimaryEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedEmail))
            return;

        if (existing is null)
        {
            await repository.AddAsync(
                CustomerEmailProjection.ForNewCustomer(
                    evt.TenantId,
                    card.CustomerId,
                    normalizedEmail,
                    card.DisplayName,
                    evt.OccurredOn
                ),
                ct
            );
            return;
        }

        // Como el alta: una ficha nueva reactiva al archivado; la edición no.
        if (
            existing.ApplyIfNewer(normalizedEmail, card.DisplayName, evt.OccurredOn)
            && card.IsNew
            && existing.IsArchived
        )
            existing.MarkReactivated(evt.OccurredOn);
    }
}
