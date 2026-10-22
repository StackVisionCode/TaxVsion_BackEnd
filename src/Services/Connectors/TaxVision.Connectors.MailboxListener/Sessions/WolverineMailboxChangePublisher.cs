using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Connectors.Application.Accounts;
using Wolverine;

namespace TaxVision.Connectors.MailboxListener.Sessions;

public sealed class WolverineMailboxChangePublisher(IServiceScopeFactory scopeFactory) : IMailboxChangePublisher
{
    public async Task PublishChangeAsync(ActiveAccountRef account, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .PublishAsync(
                new ConnectorsMailboxChangeDetectedIntegrationEvent
                {
                    TenantId = account.TenantId,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    AccountId = account.Id,
                    DetectedAtUtc = DateTime.UtcNow,
                }
            );
    }

    public async Task PublishAuthFailedAsync(ActiveAccountRef account, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .PublishAsync(
                new ConnectorsMailboxAuthFailedIntegrationEvent
                {
                    TenantId = account.TenantId,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    AccountId = account.Id,
                    FailedAtUtc = DateTime.UtcNow,
                }
            );
    }
}
