using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>
/// D-6: los agendados vivos del retirado pasan al sucesor con SU visibilidad; sin sucesor elegible se
/// pausan y se avisa (después de guardar). Lo cerrado y la procedencia no se tocan; cada paso deja constancia.
/// </summary>
public sealed class CampaignSchedulesUserOffboardedConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaverId = Guid.NewGuid();
    private readonly Guid _successorId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly FakeCampaignScheduleRepository _schedules = new();
    private readonly FakeCampaignRepository _campaigns = new();
    private readonly FakeUserPermissionsProjectionRepository _permissions = new();
    private readonly FakeMessageBus _bus = new();
    private readonly PublishOrderUnitOfWork _unitOfWork;
    private readonly Campaign _campaign;

    public CampaignSchedulesUserOffboardedConsumerTests()
    {
        _unitOfWork = new PublishOrderUnitOfWork(_bus);
        _campaign = Campaign.Create(_tenantId, _leaverId, "Spring promo", CampaignChannel.Email, "body", "subj").Value;
        _campaigns.Campaigns.Add(_campaign);
    }

    [Fact]
    public async Task Open_schedules_go_to_an_eligible_successor_with_the_successor_visibility()
    {
        _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.Send);
        var active = SeedSchedule(creatorCanViewAll: true);
        var paused = SeedSchedule(creatorCanViewAll: true);
        paused.Pause();

        await HandleAsync(Offboarded(_successorId));

        foreach (var schedule in new[] { active, paused })
        {
            Assert.Equal(_successorId, schedule.EffectiveRunAsUserId);
            Assert.Equal(_leaverId, schedule.CreatedByUserId);
            Assert.False(schedule.CreatorCanViewAllCustomers);
        }
        Assert.Equal(ScheduleStatus.Active, active.Status);
        Assert.Equal(ScheduleStatus.Paused, paused.Status);
        Assert.All(
            _schedules.Handovers,
            h =>
            {
                Assert.Equal(CampaignScheduleHandoverOutcome.Reassigned, h.Outcome);
                Assert.Equal(_leaverId, h.FromUserId);
                Assert.Equal(_successorId, h.ToUserId);
                Assert.False(h.ToCanViewAllCustomers);
                Assert.Equal(_adminId, h.PerformedByUserId);
            }
        );
        Assert.Equal(2, _schedules.Handovers.Count);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_successor_who_sees_all_clients_keeps_the_open_visibility()
    {
        _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.Send, CustomersPermissions.ViewAll);
        var schedule = SeedSchedule(creatorCanViewAll: false);

        await HandleAsync(Offboarded(_successorId));

        Assert.Equal(_successorId, schedule.RunAsUserId);
        Assert.True(schedule.CreatorCanViewAllCustomers);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("self")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("cannot-send")]
    public async Task Without_an_eligible_successor_open_schedules_are_paused_and_admins_are_told(string successorCase)
    {
        Guid? successor = successorCase switch
        {
            "none" => null,
            "self" => _leaverId,
            _ => _successorId,
        };
        if (successorCase == "inactive")
            _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.Send).MarkInactive();
        if (successorCase == "cannot-send")
            _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.View, CustomersPermissions.ViewAll);
        var schedule = SeedSchedule(creatorCanViewAll: true);

        await HandleAsync(Offboarded(successor));

        Assert.Equal(ScheduleStatus.Paused, schedule.Status);
        Assert.Equal(_leaverId, schedule.EffectiveRunAsUserId);
        Assert.Equal(CampaignScheduleHandoverOutcome.Paused, Assert.Single(_schedules.Handovers).Outcome);
        Assert.Equal([0], _unitOfWork.PublishedAtSave);
        var notice = Assert.Single(_bus.OfType<CampaignSchedulesPausedOnOffboardingIntegrationEvent>());
        Assert.Equal(_tenantId, notice.TenantId);
        Assert.Equal(_leaverId, notice.OffboardedUserId);
        Assert.Equal(_adminId, notice.OffboardedByUserId);
        Assert.Equal(
            new PausedCampaignSchedule(schedule.Id, _campaign.Id, "Spring promo"),
            Assert.Single(notice.Schedules)
        );
    }

    [Fact]
    public async Task A_redelivered_event_does_not_pause_record_or_notify_twice()
    {
        SeedSchedule(creatorCanViewAll: true);
        var evt = Offboarded(null);

        await HandleAsync(evt);
        await HandleAsync(evt);

        Assert.Single(_schedules.Handovers);
        Assert.Single(_bus.Published);
    }

    [Fact]
    public async Task Closed_schedules_other_users_and_other_offices_are_left_alone()
    {
        _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.Send);
        var fired = CampaignSchedule
            .CreateOneTime(_tenantId, _campaign.Id, DateTime.UtcNow, [], createdByUserId: _leaverId)
            .Value;
        fired.MarkFired(Guid.NewGuid(), DateTime.UtcNow);
        _schedules.Schedules.Add(fired);
        var cancelled = SeedSchedule(creatorCanViewAll: true);
        cancelled.Cancel();
        var someoneElses = SeedSchedule(creatorCanViewAll: true, creator: Guid.NewGuid());
        var otherOffice = SeedSchedule(creatorCanViewAll: true, tenantId: Guid.NewGuid());

        await HandleAsync(Offboarded(_successorId));

        Assert.All(new[] { fired, cancelled, otherOffice }, s => Assert.Equal(_leaverId, s.EffectiveRunAsUserId));
        Assert.NotEqual(_successorId, someoneElses.EffectiveRunAsUserId);
        Assert.Empty(_schedules.Handovers);
        Assert.Empty(_unitOfWork.PublishedAtSave);
    }

    [Fact]
    public async Task Schedules_inherited_by_the_leaver_move_on_to_the_next_successor()
    {
        _permissions.Seed(_tenantId, _successorId, CampaignsPermissions.Send);
        var inherited = SeedSchedule(creatorCanViewAll: true, creator: Guid.NewGuid());
        inherited.HandOverOnOffboarding(_leaverId, successorCanViewAllCustomers: true);

        await HandleAsync(Offboarded(_successorId));

        Assert.Equal(_successorId, inherited.EffectiveRunAsUserId);
        Assert.False(inherited.CreatorCanViewAllCustomers);
    }

    private CampaignSchedule SeedSchedule(bool creatorCanViewAll, Guid? creator = null, Guid? tenantId = null)
    {
        var schedule = CampaignSchedule
            .CreateRecurring(
                tenantId ?? _tenantId,
                _campaign.Id,
                DateTime.UtcNow.AddHours(1),
                RecurrenceFrequency.Daily,
                null,
                [],
                createdByUserId: creator ?? _leaverId,
                creatorCanViewAllCustomers: creatorCanViewAll
            )
            .Value;
        _schedules.Schedules.Add(schedule);
        return schedule;
    }

    private UserOffboardedIntegrationEvent Offboarded(Guid? successorId) =>
        new()
        {
            TenantId = _tenantId,
            UserId = _leaverId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            OffboardedByUserId = _adminId,
            SuccessorUserId = successorId,
            RemovedAtUtc = DateTime.UtcNow,
            CorrelationId = "offboard-correlation",
        };

    private Task HandleAsync(UserOffboardedIntegrationEvent evt) =>
        CampaignSchedulesUserOffboardedConsumer.Handle(
            evt,
            _schedules,
            _campaigns,
            _permissions,
            _unitOfWork,
            _bus,
            new FakeCorrelationContext(),
            NullLogger<CampaignSchedule>.Instance,
            CancellationToken.None
        );

    /// <summary>Anota cuántos mensajes había publicados en cada guardado (0 = guardó antes de publicar).</summary>
    private sealed class PublishOrderUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public List<int> PublishedAtSave { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            PublishedAtSave.Add(bus.Published.Count);
            return Task.FromResult(1);
        }
    }
}
