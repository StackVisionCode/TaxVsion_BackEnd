using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Audience;
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
            Campaign = TaxVision.Campaigns.Domain.Campaigns.Campaign
                .Create(Tenant, Creator, "Newsletter", CampaignChannel.Email, "body", "subj")
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

        public Task<Result<CampaignRunResponse>> FireAsync(CampaignSchedule schedule, Guid leaseToken, DateTime slot) =>
            FireCampaignScheduleHandler.Handle(
                new FireCampaignScheduleCommand(Tenant, schedule.Id, leaseToken, slot),
                Schedules,
                Campaigns,
                Runs,
                Contacts,
                Lists,
                Customers,
                new FakeCampaignCustomerAssignmentReader(),
                Microsoft.Extensions.Options.Options.Create(new CampaignsVisibilityOptions()),
                Microsoft.Extensions.Options.Options.Create(Options),
                new FakeSenderProfileRepository(),
                Wallet,
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
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
}
