using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Bloque guardado del import ⇒ cada ficha se aplica como el alta (nueva) o la edición (actualizada) del cliente,
/// con la hora del bloque para el orden. Así el correo de un importado se vincula sin esperar a la reconciliación.
/// Un cliente no activo deja de casar remitentes, como con sus eventos de estado.
/// </summary>
public static class CustomersImportChunkCommittedConsumer
{
    public static async Task<OutgoingMessages> Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerEmailAddressRepository repository,
        ITenantCustomerBackfillService backfill,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        var links = new OutgoingMessages();
        using (correlation.Push(evt.CorrelationId))
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, bus, ct);

            // El índice único de email activo no ve lo agregado en este bloque antes de guardar.
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in LatestPerCustomer(evt.Customers))
            {
                var email = EmailAddress.Create(card.PrimaryEmail ?? string.Empty);
                if (!IsActive(card))
                {
                    await DeactivateAsync(evt, card, email.IsSuccess ? email.Value : null, repository, ct);
                    continue;
                }

                // Sin email válido no hay nada que proyectar; nunca se borra el conocido.
                if (email.IsFailure || !claimed.Add(email.Value.NormalizedValue))
                    continue;

                var entered = card.IsNew
                    ? await CustomerCreatedConsumer.UpsertProjection(
                        evt.TenantId,
                        card.CustomerId,
                        evt.EventId,
                        evt.OccurredOn,
                        email.Value,
                        repository,
                        logger,
                        ct
                    )
                    : await CustomerUpdatedConsumer.UpsertProjection(
                        evt.TenantId,
                        card.CustomerId,
                        evt.EventId,
                        evt.OccurredOn,
                        email.Value,
                        repository,
                        logger,
                        ct
                    );
                if (entered)
                    links.Add(new LinkUnlinkedMailCommand(evt.TenantId, card.CustomerId, email.Value.NormalizedValue));
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Import chunk {ChunkNumber} of job {ImportJobId} projected for tenant {TenantId} ({Count} clients).",
                evt.ChunkNumber,
                evt.ImportJobId,
                evt.TenantId,
                evt.Customers.Count
            );
        }
        return links;
    }

    // Dos filas pueden tocar al mismo cliente: vale la última, y la fila agregada sin guardar no se duplica.
    private static IEnumerable<ImportedCustomerCard> LatestPerCustomer(IReadOnlyList<ImportedCustomerCard> cards) =>
        cards.GroupBy(card => card.CustomerId).Select(group => group.Last());

    private static bool IsActive(ImportedCustomerCard card) =>
        string.Equals(card.Status, "Active", StringComparison.OrdinalIgnoreCase);

    // Como Deactivated/Archived: solo la fila conocida; sin fila no hay remitente que dejar de casar.
    private static async Task DeactivateAsync(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ImportedCustomerCard card,
        EmailAddress? email,
        ICustomerEmailAddressRepository repository,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, card.CustomerId, ct);
        if (existing is null)
            return;
        if (email is not null)
            existing.UpdateEmail(email, evt.OccurredOn);
        existing.SoftDelete(evt.OccurredOn);
    }
}
