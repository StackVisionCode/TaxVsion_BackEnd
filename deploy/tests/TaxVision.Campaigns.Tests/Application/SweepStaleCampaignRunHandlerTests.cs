using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class SweepStaleCampaignRunHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static CampaignRun DispatchedRun(params string?[] emails)
    {
        var run = CampaignRun
            .Start(
                Tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                emails.Select(e => new RunRecipientDraft(Guid.NewGuid().ToString("N"), CampaignChannel.Email, e, null)).ToList()
            )
            .Value;
        foreach (var r in run.Recipients.ToList())
            run.MarkDispatched(r.Id);
        return run;
    }

    private static Task SweepAsync(CampaignRun run, FakeCampaignRunRepository runs, FakeMessageBus bus) =>
        SweepStaleCampaignRunHandler.Handle(
            new SweepStaleCampaignRunCommand(Tenant, run.Id),
            runs,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    [Fact]
    public async Task Sweep_StaleRun_PublishesRunCompletedOnce()
    {
        var run = DispatchedRun("a@x.com", "b@x.com");
        var runs = new FakeCampaignRunRepository();
        runs.Runs.Add(run);
        var bus = new FakeMessageBus();

        await SweepAsync(run, runs, bus);
        await SweepAsync(run, runs, bus);

        var completed = Assert.Single(bus.OfType<CampaignRunCompletedIntegrationEvent>());
        Assert.Equal(2, completed.Unknown);
        Assert.Equal(nameof(CampaignRunStatus.Failed), completed.TerminalStatus);
    }

    [Fact]
    public async Task Sweep_RunAlreadyClosed_PublishesNothing()
    {
        var run = DispatchedRun("a@x.com");
        run.ApplyResult(run.Recipients.Single().DispatchId, DispatchOutcome.Delivered, null, null);
        var runs = new FakeCampaignRunRepository();
        runs.Runs.Add(run);
        var bus = new FakeMessageBus();

        await SweepAsync(run, runs, bus);

        Assert.Empty(bus.Published);
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(1, run.CounterDelivered);
    }
}
