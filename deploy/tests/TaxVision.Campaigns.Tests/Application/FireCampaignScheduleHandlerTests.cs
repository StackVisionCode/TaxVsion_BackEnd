using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class FireCampaignScheduleHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeCampaignScheduleRepository Schedules { get; } = new();
        public FakeCampaignRepository Campaigns { get; } = new();
        public FakeCampaignRunRepository Runs { get; } = new();
        public FakeContactRepository Contacts { get; } = new();
        public FakeContactListRepository Lists { get; } = new();
        public FakeCustomerAudienceClient Customers { get; } = new();
        public FakeWalletSpendClient Wallet { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public CampaignSchedulerOptions Options { get; } = new();
        public Campaign Campaign { get; }
        public ContactList List { get; }

        public Harness()
        {
            TestRunFlow.Route(Bus, Runs, Campaigns, Schedules, UnitOfWork);
            Campaign = TaxVision
                .Campaigns.Domain.Campaigns.Campaign.Create(
                    Tenant,
                    Creator,
                    "Newsletter",
                    CampaignChannel.Email,
                    "body",
                    "subj"
                )
                .Value;
            Campaigns.Campaigns.Add(Campaign);
            List = ContactList.Create(Tenant, "L", null).Value;
            Lists.Seed(List);
        }

        public Contact AddContact(string email)
        {
            var contact = Contact.Create(Tenant, null, email, null, ContactSource.Manual).Value;
            Contacts.Seed(contact);
            List.AddMember(contact.Id);
            return contact;
        }

        public CampaignSchedule AddRecurring(DateTime slot, bool includeCustomers = false)
        {
            var schedule = CampaignSchedule
                .CreateRecurring(
                    Tenant,
                    Campaign.Id,
                    slot,
                    RecurrenceFrequency.Hourly,
                    null,
                    [List.Id],
                    includeCustomers,
                    createdByUserId: Creator
                )
                .Value;
            Schedules.Schedules.Add(schedule);
            return schedule;
        }

        public CampaignSchedule AddOneTime(DateTime slot)
        {
            var schedule = CampaignSchedule
                .CreateOneTime(Tenant, Campaign.Id, slot, [List.Id], createdByUserId: Creator)
                .Value;
            Schedules.Schedules.Add(schedule);
            return schedule;
        }

        public Task<Result<CampaignRunResponse>> FireAsync(CampaignSchedule schedule, Guid leaseToken, DateTime slot) =>
            FireCampaignScheduleHandler.Handle(
                new FireCampaignScheduleCommand(Tenant, schedule.Id, leaseToken, slot),
                Schedules,
                new ScheduledRunOpener(
                    Campaigns,
                    Runs,
                    new CampaignAudienceService(
                        Contacts,
                        Lists,
                        Customers,
                        new FakeCampaignCustomerAssignmentReader(),
                        Microsoft.Extensions.Options.Options.Create(new CampaignsVisibilityOptions())
                    )
                ),
                Microsoft.Extensions.Options.Options.Create(Options),
                new CampaignRunStarter(Wallet, new FakeCorrelationContext(), NullLogger<CampaignRunStarter>.Instance),
                Bus,
                NullLogger<CampaignSchedule>.Instance,
                CancellationToken.None
            );
    }

    [Fact]
    public async Task Fire_CreatesRunAndMarksFiredInSameHandler()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.AddContact("beto@x.com");
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.True(result.IsSuccess);
        var run = Assert.Single(h.Runs.Runs);
        Assert.Equal(schedule.Id, run.ScheduleId);
        Assert.Equal(run.Id, schedule.ActiveRunId);
        Assert.True(schedule.NextFireAtUtc > slot);
        Assert.Null(schedule.LeaseToken);
        Assert.Equal(2, h.Bus.OfType<CampaignDispatchRequestedIntegrationEvent>().Count());
        // La reserva usa el Id determinista del run: estable entre reintentos del mismo slot.
        Assert.Equal([run.Id], h.Wallet.ReservedReferences);
    }

    [Fact]
    public async Task Fire_LeaseHeldByAnotherClaim_DoesNothing()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var leaseB = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, Guid.NewGuid(), slot);

        Assert.True(result.IsFailure);
        Assert.Empty(h.Runs.Runs);
        Assert.Empty(h.Bus.Published);
        Assert.Equal(slot, schedule.NextFireAtUtc);
        Assert.Equal(leaseB, schedule.LeaseToken);
        Assert.Equal(0, schedule.OccurrenceCount);
    }

    [Fact]
    public async Task Fire_AllUnitsSkipped_ScheduleStaysClaimable()
    {
        var h = new Harness();
        h.AddContact("ana@x.com").OptOut(CampaignChannel.Email);
        h.AddContact("beto@x.com").OptOut(CampaignChannel.Email);
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        await h.FireAsync(schedule, lease, slot);

        // El resolver excluye a los dados de baja: no hay unidades, no se crea run y el agendado avanza libre.
        Assert.Empty(h.Bus.OfType<CampaignDispatchRequestedIntegrationEvent>());
        Assert.Null(schedule.ActiveRunId);
        Assert.True(schedule.IsClaimable(schedule.NextFireAtUtc!.Value));
    }

    [Fact]
    public async Task Fire_AudienceIncomplete_DoesNotAdvanceSchedule()
    {
        var h = new Harness();
        h.Customers.IsComplete = false;
        h.Customers.Seed("a@x.com", null);
        var slot = DateTime.UtcNow.AddMinutes(-10);
        var schedule = h.AddRecurring(slot, includeCustomers: true);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        await Assert.ThrowsAsync<CustomerAudienceUnavailableException>(() => h.FireAsync(schedule, lease, slot));

        // Transitorio: rollback, el lease vence y el próximo claim reintenta el mismo slot.
        Assert.Equal(slot, schedule.NextFireAtUtc);
        Assert.Equal(0, schedule.OccurrenceCount);
        Assert.Empty(h.Runs.Runs);
        Assert.Empty(h.Bus.Published);
    }

    [Fact]
    public async Task Fire_AudienceIncompleteBeyondMaxDelay_SkipsSlot()
    {
        var h = new Harness();
        h.Customers.IsComplete = false;
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddRecurring(slot, includeCustomers: true);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.True(result.IsFailure);
        Assert.Equal(CampaignRunErrors.AudienceIncomplete, result.Error);
        Assert.True(schedule.NextFireAtUtc > DateTime.UtcNow);
        Assert.Null(schedule.ActiveRunId);
        Assert.Empty(h.Runs.Runs);
    }

    [Fact]
    public async Task Fire_WalletUnavailable_DoesNotAdvanceSlot()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.Equal(CampaignRunErrors.WalletUnavailable, result.Error);
        Assert.Equal(slot, schedule.NextFireAtUtc);
        Assert.Equal(0, schedule.OccurrenceCount);
        Assert.Null(schedule.LastFiredAtUtc);
        // El run espera en Created: el próximo claim lo retoma con la misma referencia.
        Assert.Equal(CampaignRunStatus.Created, Assert.Single(h.Runs.Runs).Status);
        Assert.Empty(h.Bus.Published);
    }

    [Fact]
    public async Task Fire_OneTimeWalletUnavailable_StaysActive()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddOneTime(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        await h.FireAsync(schedule, lease, slot);

        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Equal(slot, schedule.NextFireAtUtc);
    }

    [Fact]
    public async Task Fire_WalletBackOnNextClaim_FiresSameSlotWithSameReference()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.AddContact("beto@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddOneTime(slot);
        await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        h.Wallet.ReserveResult = new WalletReserveResult(true, true, 0, 0, 0, "USD");
        var result = await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        Assert.True(result.IsSuccess);
        var run = Assert.Single(h.Runs.Runs);
        Assert.Equal(ScheduledRunId.For(schedule.Id, slot), run.Id);
        Assert.Equal([run.Id, run.Id], h.Wallet.ReservedReferences);
        Assert.Equal(2, h.Bus.OfType<CampaignDispatchRequestedIntegrationEvent>().Count());
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Equal(1, schedule.OccurrenceCount);
    }

    [Fact]
    public async Task Fire_InsufficientFunds_AdvancesWithRejectedRun()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = new WalletReserveResult(true, false, 500, 100, 400, "USD");
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddOneTime(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.True(result.IsFailure);
        Assert.Equal(CampaignRunStatus.Rejected, Assert.Single(h.Runs.Runs).Status);
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Empty(h.Bus.Published);
    }

    [Fact]
    public async Task Fire_WalletUnavailableBeyondMaxDelay_SkipsSlotWithRejectedRun()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddRecurring(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.Equal(CampaignRunErrors.WalletUnavailable, result.Error);
        var run = Assert.Single(h.Runs.Runs);
        Assert.Equal(CampaignRunStatus.Rejected, run.Status);
        Assert.Equal("wallet_unavailable", run.RejectionReason);
        Assert.True(schedule.NextFireAtUtc > DateTime.UtcNow);
        Assert.Null(schedule.ActiveRunId);
        Assert.Equal(run.Id, Assert.Single(h.Bus.OfType<ReleaseCampaignRunReservationCommand>()).RunId);
    }

    // Decisión del dueño (2026-11-06): un OneTime no se completa sin enviar; la oficina decide.
    [Fact]
    public async Task Fire_OneTimeWalletUnavailableBeyondMaxDelay_PausesForTheOffice()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddOneTime(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        var result = await h.FireAsync(schedule, lease, slot);

        Assert.Equal(CampaignRunErrors.WalletUnavailable, result.Error);
        Assert.Equal(ScheduleStatus.Paused, schedule.Status);
        Assert.Equal(slot, schedule.NextFireAtUtc);
        Assert.Equal(0, schedule.OccurrenceCount);
        Assert.Empty(h.Bus.Published);
        Assert.Equal(CampaignRunStatus.Created, Assert.Single(h.Runs.Runs).Status);
    }

    // CMP-19: la oficina ve por qué quedó pausado, no solo que lo está.
    [Fact]
    public async Task Fire_OneTimeWalletUnavailable_SaysWhyItPaused()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddOneTime(slot);

        await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        Assert.Equal("WalletUnavailable", CampaignScheduleResponse.From(schedule).PauseReason);
    }

    [Fact]
    public async Task Fire_OneTimePausedThenResumed_SendsTheSameSlotOnce()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = WalletReserveResult.Unreachable();
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddOneTime(slot);
        await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        schedule.Resume();
        h.Wallet.ReserveResult = new WalletReserveResult(true, true, 0, 0, 0, "USD");
        var result = await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        Assert.True(result.IsSuccess);
        var run = Assert.Single(h.Runs.Runs);
        Assert.Equal(CampaignRunStatus.Dispatching, run.Status);
        Assert.Equal([run.Id, run.Id], h.Wallet.ReservedReferences);
        Assert.Single(h.Bus.OfType<CampaignDispatchRequestedIntegrationEvent>());
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
    }

    // Sin saldo no cambia: el OneTime cierra con el run rechazado, aunque el slot sea viejo.
    [Fact]
    public async Task Fire_OneTimeInsufficientFundsBeyondMaxDelay_StillCompletes()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        h.Wallet.ReserveResult = new WalletReserveResult(true, false, 500, 100, 400, "USD");
        var slot = DateTime.UtcNow.AddHours(-7);
        var schedule = h.AddOneTime(slot);

        await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Equal(CampaignRunStatus.Rejected, Assert.Single(h.Runs.Runs).Status);
    }

    // Lo que ya salió no se repite: un slot con su run cerrado solo avanza.
    [Fact]
    public async Task Fire_SlotWithAClosedRun_AdvancesWithoutSending()
    {
        var h = new Harness();
        var contact = h.AddContact("ana@x.com");
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var run = CampaignRun
            .StartScheduled(
                Tenant,
                h.Campaign.Id,
                Creator,
                schedule.Id,
                slot,
                [new RunRecipientDraft(contact.Id.ToString("N"), CampaignChannel.Email, "ana@x.com", null)]
            )
            .Value;
        run.Reject("wallet_unavailable");
        h.Runs.Runs.Add(run);

        var result = await h.FireAsync(schedule, ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2)), slot);

        Assert.Equal(CampaignRunErrors.NotCreated, result.Error);
        Assert.True(schedule.NextFireAtUtc > slot);
        Assert.Empty(h.Wallet.ReservedReferences);
        Assert.Empty(h.Bus.Published);
    }

    [Fact]
    public async Task Fire_SaveFails_PublishesNoDispatch()
    {
        var h = new Harness();
        h.AddContact("ana@x.com");
        var slot = DateTime.UtcNow.AddMinutes(-1);
        var schedule = h.AddRecurring(slot);
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));
        h.UnitOfWork.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.FireAsync(schedule, lease, slot));

        Assert.Empty(h.Bus.Published);
    }
}
