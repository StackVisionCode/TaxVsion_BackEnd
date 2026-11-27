using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

/// <summary>
/// Conexión (o reconexión) de un buzón ⇒ inserta o reactiva la proyección local que
/// <c>IConnectedMailboxResolver</c> consulta para armar el canal de envío TenantMailbox (D3 §4.3).
/// Idempotente: si ya existe la fila para ese <c>AccountId</c>, se reconcilia en vez de duplicar.
/// </summary>
public static class TenantEmailAccountConnectedConsumer
{
    public static async Task Handle(
        ConnectorsTenantEmailAccountConnectedIntegrationEvent evt,
        IConnectedMailboxRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ConnectedMailbox> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            // Dueño según lo que declara Connectors; un aviso viejo o contradictorio queda por reconciliar.
            var (ownershipKnown, ownerUserId) = MailboxOwnership.Resolve(evt.OwnerUserId, evt.IsOffice);
            var existing = await repository.GetByAccountIdAsync(evt.TenantId, evt.AccountId, ct);
            if (existing is not null)
            {
                if (
                    !existing.ReconnectAt(
                        evt.ProviderCode,
                        evt.EmailAddress,
                        evt.ConnectedAtUtc,
                        ownerUserId,
                        ownershipKnown
                    )
                )
                {
                    logger.LogInformation(
                        "ConnectedMailbox {AccountId}: connected event from {ConnectedAtUtc} is older than the last update; ignored.",
                        evt.AccountId,
                        evt.ConnectedAtUtc
                    );
                    return;
                }

                logger.LogInformation(
                    "ConnectedMailbox {AccountId} already projected for tenant {TenantId}; reconciled as reconnected.",
                    evt.AccountId,
                    evt.TenantId
                );
            }
            else
            {
                var account = ConnectedMailbox.ForNewConnection(
                    evt.TenantId,
                    evt.AccountId,
                    evt.ProviderCode,
                    evt.EmailAddress,
                    evt.ConnectedAtUtc,
                    ownerUserId,
                    ownershipKnown
                );
                await repository.AddAsync(account, ct);
                logger.LogInformation(
                    "ConnectedMailbox {AccountId} projected for tenant {TenantId} (provider={ProviderCode}).",
                    evt.AccountId,
                    evt.TenantId,
                    evt.ProviderCode
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
