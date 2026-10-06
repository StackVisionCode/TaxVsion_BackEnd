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
/// revocado (D3 §4.3).
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
                logger.LogInformation(
                    "ConnectedMailbox {AccountId} not found for tenant {TenantId}; nothing to disconnect.",
                    evt.AccountId,
                    evt.TenantId
                );
                return;
            }

            existing.MarkDisconnected(evt.DisconnectedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
