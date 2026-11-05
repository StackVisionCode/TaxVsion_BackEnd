using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Runs.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class CampaignSmsAcceptedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Handle_UnknownUnitResolvedAsAccepted_AppliesAccepted()
    {
        var run = CampaignRun
            .Start(
                Tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [new RunRecipientDraft("c1", CampaignChannel.Sms, null, "+18290000001")]
            )
            .Value;
        var unit = run.Recipients.Single();
        run.MarkDispatched(unit.Id);
        // Sms reportó Unknown (Pending/OutcomeUnknown) y el run cerró con esa unidad.
        run.ApplyResult(unit.DispatchId, DispatchOutcome.Unknown, null, "outcome_unknown");
        var runs = new FakeCampaignRunRepository();
        runs.Runs.Add(run);
        var unitOfWork = new FakeUnitOfWork();

        // La conciliación de Sms lo resuelve como aceptado (proveedor sin DLR): solo publica SmsMessageAccepted.
        await CampaignSmsAcceptedConsumer.Handle(
            new SmsMessageAcceptedIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "test",
                MessageId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                SourceContext = $"campaign:{unit.DispatchId}",
                ProviderMessageId = "prov-1",
            },
            runs,
            unitOfWork,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<CampaignRun>.Instance,
            CancellationToken.None
        );

        Assert.Equal(DispatchState.Accepted, unit.State);
        Assert.Equal("prov-1", unit.ProviderRef);
        Assert.Equal(1, run.CounterAccepted);
        Assert.Equal(0, run.CounterUnknown);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
