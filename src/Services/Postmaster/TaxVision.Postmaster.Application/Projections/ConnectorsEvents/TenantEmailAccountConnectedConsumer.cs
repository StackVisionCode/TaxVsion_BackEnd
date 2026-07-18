using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

/// <summary>
/// Conexión (o reconexión) de cuenta OAuth ⇒ inserta o reactiva la proyección local que
/// <c>IOAuthProviderResolver</c> consulta para armar el canal de envío TenantOAuth (D3 §4.3).
/// Idempotente: si ya existe la fila para ese <c>AccountId</c>, se reconcilia en vez de duplicar.
/// </summary>
public static class TenantEmailAccountConnectedConsumer
{
    public static async Task Handle(
        ConnectorsTenantEmailAccountConnectedIntegrationEvent evt,
        ITenantOAuthAccountRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantOAuthAccount> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var existing = await repository.GetByAccountIdAsync(evt.TenantId, evt.AccountId, ct);
            if (existing is not null)
            {
                existing.ReconnectAt(evt.ProviderCode, evt.EmailAddress, evt.ConnectedAtUtc);
                logger.LogInformation(
                    "TenantOAuthAccount {AccountId} already projected for tenant {TenantId}; reconciled as reconnected.",
                    evt.AccountId,
                    evt.TenantId
                );
            }
            else
            {
                var account = TenantOAuthAccount.ForNewConnection(
                    evt.TenantId,
                    evt.AccountId,
                    evt.ProviderCode,
                    evt.EmailAddress,
                    evt.ConnectedAtUtc
                );
                await repository.AddAsync(account, ct);
                logger.LogInformation(
                    "TenantOAuthAccount {AccountId} projected for tenant {TenantId} (provider={ProviderCode}).",
                    evt.AccountId,
                    evt.TenantId,
                    evt.ProviderCode
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
