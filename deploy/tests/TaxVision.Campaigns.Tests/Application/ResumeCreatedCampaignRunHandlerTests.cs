using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class ResumeCreatedCampaignRunHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly FakeCampaignRunRepository _runs = new();
    private readonly FakeWalletSpendClient _wallet = new();
    private readonly FakeTenantEntitlementModulesReader _modules = new();
    private readonly FakeMessageBus _bus = new();
    private readonly List<object> _invoked = [];

    public ResumeCreatedCampaignRunHandlerTests() =>
        _bus.OnInvoke = message =>
        {
            _invoked.Add(message);
            return Task.FromResult<object?>(null);
        };

    // Oficina sin el módulo: el run sigue en Created, sin reservar en el Wallet ni despachar.
    [Fact]
    public async Task A_created_run_without_the_module_is_not_reserved_nor_dispatched()
    {
        _modules.Modules = ["clients"];
        var run = SeedCreatedRun();

        await HandleAsync(run);

        Assert.Empty(_wallet.ReservedReferences);
        Assert.Empty(_invoked);
        Assert.Equal(CampaignRunStatus.Created, run.Status);
    }

    [Fact]
    public async Task A_created_run_with_the_module_is_reserved_and_applied()
    {
        _modules.Modules = ["campaigns"];
        var run = SeedCreatedRun();

        await HandleAsync(run);

        Assert.Equal([run.Id], _wallet.ReservedReferences);
        Assert.IsType<ApplyCampaignRunFundingCommand>(Assert.Single(_invoked));
    }

    private CampaignRun SeedCreatedRun()
    {
        var run = CampaignRun
            .Start(
                Tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [new RunRecipientDraft("c", CampaignChannel.Email, "ana@x.com", null)]
            )
            .Value;
        _runs.Runs.Add(run);
        return run;
    }

    private Task HandleAsync(CampaignRun run) =>
        ResumeCreatedCampaignRunHandler.Handle(
            new ResumeCreatedCampaignRunCommand(Tenant, run.Id),
            _runs,
            new FakeCampaignScheduleRepository(),
            Microsoft.Extensions.Options.Options.Create(new CampaignSchedulerOptions()),
            new CampaignRunStarter(
                _wallet,
                new FakeCampaignRepository(),
                new FakeCorrelationContext(),
                _modules,
                NullLogger<CampaignRunStarter>.Instance
            ),
            _bus,
            NullLogger<CampaignRun>.Instance,
            CancellationToken.None
        );
}
