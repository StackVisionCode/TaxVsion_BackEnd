using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

/// <summary>
/// Desconexión de un buzón ⇒ da de baja la proyección local para que un envío TenantMailbox
/// posterior falle limpio (<c>ProviderNotConfigured</c>) en vez de intentar contra un token ya
/// revocado (D3 §4.3). Sin fila deja una lápida: el alta más vieja que llegue después no la reactiva.
/// </summary>
public static class TenantEmailAccountDisconnectedConsumer
{
    public static async Task Handle(
        ConnectorsTenantEmailAccountDisconnectedIntegrationEvent evt,
        IConnectedMailboxRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ConnectedMailbox> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var existing = await repository.GetByAccountIdAsync(evt.TenantId, evt.AccountId, ct);
            if (existing is null)
            {
                await repository.AddAsync(
                    ConnectedMailbox.Tombstone(
                        evt.TenantId,
                        evt.AccountId,
                        evt.ProviderCode,
                        evt.EmailAddress,
                        evt.DisconnectedAtUtc
                    ),
                    ct
                );
                logger.LogInformation(
                    "ConnectedMailbox {AccountId} not found for tenant {TenantId}; tombstone left as disconnected.",
                    evt.AccountId,
                    evt.TenantId
                );
            }
            else if (!existing.MarkDisconnected(evt.DisconnectedAtUtc))
            {
                logger.LogInformation(
                    "ConnectedMailbox {AccountId}: disconnected event from {DisconnectedAtUtc} is older than the last update; ignored.",
                    evt.AccountId,
                    evt.DisconnectedAtUtc
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
