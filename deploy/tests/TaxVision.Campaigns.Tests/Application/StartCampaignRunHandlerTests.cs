using System.Reflection;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>CMP-05 (BB-8): el run existe antes de llamar al Wallet y la reserva nunca cambia de referencia.</summary>
public sealed class StartCampaignRunHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly FakeCampaignRepository _campaigns = new();
    private readonly FakeCampaignRunRepository _runs = new();
    private readonly FakeCampaignScheduleRepository _schedules = new();
    private readonly FakeWalletSpendClient _wallet = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();
    private readonly CampaignSchedulerOptions _schedulerOptions = new();
    private readonly Campaign _campaign;

    public StartCampaignRunHandlerTests()
    {
        _campaign = Campaign.Create(Tenant, Actor, "Newsletter", CampaignChannel.Email, "body", "subj").Value;
        _campaigns.Campaigns.Add(_campaign);
        TestRunFlow.Route(_bus, _runs, _campaigns, _schedules, _unitOfWork);
    }

    private Task<Result<CampaignRunResponse>> StartAsync() =>
        StartCampaignRunHandler.Handle(
            new StartCampaignRunCommand(
                Tenant,
                _campaign.Id,
                Actor,
                [new StartRunRecipient("c1", "ana@x.com", null), new StartRunRecipient("c2", "beto@x.com", null)]
            ),
            _campaigns,
            _wallet,
            _bus,
            new FakeCorrelationContext(),
            NullLogger<CampaignRun>.Instance,
            CancellationToken.None
        );

    private Task ResumeAsync(Guid runId) =>
        ResumeCreatedCampaignRunHandler.Handle(
            new ResumeCreatedCampaignRunCommand(Tenant, runId),
            _runs,
            _schedules,
            Options.Create(_schedulerOptions),
            _wallet,
            _bus,
            NullLogger<CampaignRun>.Instance,
            CancellationToken.None
        );

    [Theory]
    [InlineData(typeof(StartCampaignRunHandler), typeof(StartCampaignRunCommand))]
    [InlineData(typeof(StartCampaignRunFromAudienceHandler), typeof(StartCampaignRunFromAudienceCommand))]
    [InlineData(typeof(FireCampaignScheduleHandler), typeof(FireCampaignScheduleCommand))]
    [InlineData(typeof(ResumeCreatedCampaignRunHandler), typeof(ResumeCreatedCampaignRunCommand))]
    public void Wallet_call_runs_outside_a_transaction(Type handler, Type command)
    {
        var handle = handler
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == command);

        Assert.NotNull(handle.GetCustomAttribute<NonTransactionalAttribute>());
        Assert.DoesNotContain(handle.GetParameters(), parameter => parameter.ParameterType == typeof(IUnitOfWork));
    }

    [Fact]
    public async Task Run_is_committed_before_the_wallet_is_called()
    {
        var savedAtReserve = 0;
        var statusAtReserve = CampaignRunStatus.Failed;
        _wallet.OnReserve = () =>
        {
            savedAtReserve = _unitOfWork.SaveChangesCallCount;
            statusAtReserve = Assert.Single(_runs.Runs).Status;
        };

        var result = await StartAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, savedAtReserve);
        Assert.Equal(CampaignRunStatus.Created, statusAtReserve);
        Assert.Equal(CampaignRunStatus.Dispatching, Assert.Single(_runs.Runs).Status);
    }

    // Plantilla BB-8: el Wallet aceptó, el guardado falla y el barrido retoma sin otra reserva.
    [Fact]
    public async Task Start_FailsAfterWalletReserved_RetryReusesSameReference()
    {
        var route = _bus.OnInvoke!;
        _bus.OnInvoke = message =>
            message is ApplyCampaignRunFundingCommand
                ? throw new InvalidOperationException("Simulated save failure.")
                : route(message);

        var result = await StartAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(CampaignRunStatus.Created), result.Value.Status);
        Assert.Empty(_bus.OfType<CampaignDispatchRequestedIntegrationEvent>());

        _bus.OnInvoke = route;
        await ResumeAsync(result.Value.Id);

        var run = Assert.Single(_runs.Runs);
        Assert.Equal([run.Id, run.Id], _wallet.ReservedReferences);
        Assert.Equal(CampaignRunStatus.Dispatching, run.Status);
        Assert.Equal(2, _bus.OfType<CampaignDispatchRequestedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task WalletDown_RejectsTheRunAndReleasesWhateverWasReserved()
    {
        _wallet.ReserveResult = WalletReserveResult.Unreachable();

        var result = await StartAsync();

        Assert.Equal(CampaignRunErrors.WalletUnavailable, result.Error);
        var run = Assert.Single(_runs.Runs);
        Assert.Equal(CampaignRunStatus.Rejected, run.Status);
        Assert.Equal(run.Id, Assert.Single(_bus.OfType<ReleaseCampaignRunReservationCommand>()).RunId);
    }

    [Fact]
    public async Task InsufficientFunds_RejectsWithoutRelease()
    {
        _wallet.ReserveResult = new WalletReserveResult(true, false, 500, 100, 400, "USD");

        var result = await StartAsync();

        Assert.Equal("CampaignRun.InsufficientFunds", result.Error.Code);
        Assert.Equal(CampaignRunStatus.Rejected, Assert.Single(_runs.Runs).Status);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Resume_WalletStillDown_KeepsWaitingWithinTheWindow()
    {
        var run = CreatedRun();
        _wallet.ReserveResult = WalletReserveResult.Unreachable();

        await ResumeAsync(run.Id);

        Assert.Equal(CampaignRunStatus.Created, run.Status);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Resume_WalletDownPastTheWindow_RejectsAndReleases()
    {
        var run = CreatedRun();
        _schedulerOptions.MaxFireDelay = TimeSpan.FromTicks(1);
        _wallet.ReserveResult = WalletReserveResult.Unreachable();

        await ResumeAsync(run.Id);

        Assert.Equal(CampaignRunStatus.Rejected, run.Status);
        Assert.Equal(run.Id, Assert.Single(_bus.OfType<ReleaseCampaignRunReservationCommand>()).RunId);
    }

    [Fact]
    public async Task Resume_CampaignArchived_RejectsAndReleases()
    {
        var run = CreatedRun();
        _campaign.Archive();

        await ResumeAsync(run.Id);

        Assert.Equal(CampaignRunStatus.Rejected, run.Status);
        Assert.Equal("campaign_archived", run.RejectionReason);
        Assert.Single(_bus.OfType<ReleaseCampaignRunReservationCommand>());
    }

    [Theory]
    [InlineData(ScheduleStatus.Active)]
    [InlineData(ScheduleStatus.Paused)]
    public async Task Resume_ScheduledRunOfALiveSchedule_IsLeftToItsNextClaim(ScheduleStatus status)
    {
        var (schedule, run) = ScheduledRun();
        if (status == ScheduleStatus.Paused)
            schedule.Pause();

        await ResumeAsync(run.Id);

        Assert.Equal(CampaignRunStatus.Created, run.Status);
        Assert.Empty(_wallet.ReservedReferences);
    }

    [Fact]
    public async Task Resume_ScheduledRunOfACancelledSchedule_RejectsAndReleases()
    {
        var (schedule, run) = ScheduledRun();
        schedule.Cancel();

        await ResumeAsync(run.Id);

        Assert.Equal(CampaignRunStatus.Rejected, run.Status);
        Assert.Empty(_wallet.ReservedReferences);
        Assert.Single(_bus.OfType<ReleaseCampaignRunReservationCommand>());
    }

    [Fact]
    public async Task Release_SettlesNothingConsumed()
    {
        var runId = Guid.NewGuid();

        await ReleaseCampaignRunReservationHandler.Handle(
            new ReleaseCampaignRunReservationCommand(Tenant, runId),
            _wallet,
            CancellationToken.None
        );

        Assert.Equal((runId, 0), Assert.Single(_wallet.Settlements));
    }

    private static RunRecipientDraft Unit() => new("c1", CampaignChannel.Email, "ana@x.com", null);

    private CampaignRun CreatedRun()
    {
        var run = CampaignRun.Start(Tenant, _campaign.Id, Actor, "Manual", [Unit()]).Value;
        _runs.Runs.Add(run);
        return run;
    }

    private (CampaignSchedule Schedule, CampaignRun Run) ScheduledRun()
    {
        var slot = DateTime.UtcNow.AddMinutes(-30);
        var schedule = CampaignSchedule.CreateOneTime(Tenant, _campaign.Id, slot, [], createdByUserId: Actor).Value;
        _schedules.Schedules.Add(schedule);
        var run = CampaignRun.StartScheduled(Tenant, _campaign.Id, Actor, schedule.Id, slot, [Unit()]).Value;
        _runs.Runs.Add(run);
        return (schedule, run);
    }
}
