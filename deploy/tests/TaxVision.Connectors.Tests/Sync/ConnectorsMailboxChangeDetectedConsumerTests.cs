using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Sync;

public sealed class ConnectorsMailboxChangeDetectedConsumerTests
{
    [Fact]
    public async Task Handle_RunsTheUsualReconciliationForTheAccount()
    {
        var accountId = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeAnyResult = Result.Success(new ReconciliationOutcome(1, CursorWasSeeded: false, Skipped: false)),
        };
        var correlation = new FakeCorrelationContext();

        await ConnectorsMailboxChangeDetectedConsumer.Handle(
            new ConnectorsMailboxChangeDetectedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                CorrelationId = "listener-1",
                AccountId = accountId,
                DetectedAtUtc = DateTime.UtcNow,
            },
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        var command = Assert.IsType<ReconcileAccountCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(accountId, command.AccountId);
        // La correlación del aviso se suelta al terminar.
        Assert.Equal(string.Empty, correlation.CorrelationId);
    }
}
