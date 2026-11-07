using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Accounts.Consumers;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>El evento de retiro solo reenvía la purga de los buzones personales a su comando sin transacción.</summary>
public sealed class ConnectorsUserOffboardedConsumerTests
{
    [Fact]
    public async Task Offboarding_forwards_the_purge_to_its_command()
    {
        var bus = new FakeMessageBus();
        var evt = new UserOffboardedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
            CorrelationId = "corr-1",
        };

        await ConnectorsUserOffboardedConsumer.Handle(evt, bus);

        Assert.Equal(
            new PurgeOffboardedMailboxesCommand(evt.TenantId, evt.UserId, "corr-1"),
            Assert.Single(bus.Published)
        );
    }
}
