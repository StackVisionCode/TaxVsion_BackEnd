using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>
/// Reanudar un agendado cuyo ejecutor ya no puede enviar (retirado o sin permiso) lo pasa a quien reanuda,
/// con la visibilidad de este, y deja constancia; si quien reanuda tampoco puede enviar, se rechaza. Con
/// el ejecutor elegible, Resume es el de siempre.
/// </summary>
public sealed class SetScheduleStateHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _runAsId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly FakeCampaignScheduleRepository _schedules = new();
    private readonly FakeUserPermissionsProjectionRepository _permissions = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task An_eligible_sender_keeps_the_schedule_on_resume()
    {
        _permissions.Seed(_tenantId, _runAsId, CampaignsPermissions.Send);
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.Send, CustomersPermissions.ViewAll);
        var schedule = SeedPaused(creatorCanViewAll: false);

        var result = await ResumeAsync(schedule);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Null(schedule.RunAsUserId);
        Assert.Equal(_runAsId, schedule.EffectiveRunAsUserId);
        Assert.False(schedule.CreatorCanViewAllCustomers);
        Assert.Empty(_schedules.Handovers);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task An_offboarded_sender_hands_the_schedule_to_the_actor_with_the_actor_visibility()
    {
        _permissions.Seed(_tenantId, _runAsId, CampaignsPermissions.Send).MarkInactive();
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.Send, CustomersPermissions.ViewAll);
        var schedule = SeedPaused(creatorCanViewAll: false);

        var result = await ResumeAsync(schedule);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Equal(_actorId, schedule.EffectiveRunAsUserId);
        Assert.Equal(_runAsId, schedule.CreatedByUserId);
        Assert.True(schedule.CreatorCanViewAllCustomers);
        var handover = Assert.Single(_schedules.Handovers);
        Assert.Equal(CampaignScheduleHandoverOutcome.ResumedByAdmin, handover.Outcome);
        Assert.Equal(schedule.Id, handover.ScheduleId);
        Assert.Equal(_runAsId, handover.FromUserId);
        Assert.Equal(_actorId, handover.ToUserId);
        Assert.True(handover.ToCanViewAllCustomers);
        Assert.Equal(_actorId, handover.PerformedByUserId);
        Assert.Null(handover.SourceEventId);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_sender_without_send_permission_is_replaced_and_the_actor_limited_view_applies()
    {
        _permissions.Seed(_tenantId, _runAsId, CampaignsPermissions.View);
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.Send);
        var schedule = SeedPaused(creatorCanViewAll: true);

        var result = await ResumeAsync(schedule);

        Assert.True(result.IsSuccess);
        Assert.Equal(_actorId, schedule.RunAsUserId);
        Assert.False(schedule.CreatorCanViewAllCustomers);
        Assert.False(Assert.Single(_schedules.Handovers).ToCanViewAllCustomers);
    }

    [Fact]
    public async Task An_inherited_schedule_compares_the_heir_not_the_creator()
    {
        var heirId = Guid.NewGuid();
        _permissions.Seed(_tenantId, _runAsId, CampaignsPermissions.Send);
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.Send);
        var schedule = SeedPaused(creatorCanViewAll: true);
        schedule.HandOverOnOffboarding(heirId, successorCanViewAllCustomers: true);

        var result = await ResumeAsync(schedule);

        Assert.True(result.IsSuccess);
        Assert.Equal(_actorId, schedule.RunAsUserId);
        Assert.Equal(heirId, Assert.Single(_schedules.Handovers).FromUserId);
    }

    [Fact]
    public async Task An_actor_who_cannot_send_either_is_rejected_and_nothing_changes()
    {
        _permissions.Seed(_tenantId, _runAsId, CampaignsPermissions.Send).MarkInactive();
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.View);
        var schedule = SeedPaused(creatorCanViewAll: false);

        var result = await ResumeAsync(schedule);

        Assert.Equal(CampaignScheduleErrors.ResumeTakeoverNotAllowed, result.Error);
        Assert.Equal(ScheduleStatus.Paused, schedule.Status);
        Assert.Equal(_runAsId, schedule.EffectiveRunAsUserId);
        Assert.Empty(_schedules.Handovers);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task An_actor_without_a_projection_in_the_office_is_rejected()
    {
        var schedule = SeedPaused(creatorCanViewAll: false);

        var result = await ResumeAsync(schedule);

        Assert.Equal(CampaignScheduleErrors.ResumeTakeoverNotAllowed, result.Error);
        Assert.Equal(ScheduleStatus.Paused, schedule.Status);
    }

    [Fact]
    public async Task A_schedule_without_a_recorded_sender_resumes_as_before()
    {
        var schedule = CampaignSchedule.CreateOneTime(_tenantId, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), []).Value;
        schedule.Pause();
        _schedules.Schedules.Add(schedule);

        var result = await ResumeAsync(schedule);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.Null(schedule.EffectiveRunAsUserId);
        Assert.Empty(_schedules.Handovers);
    }

    [Fact]
    public async Task Resuming_an_active_schedule_fails_without_taking_it_over()
    {
        _permissions.Seed(_tenantId, _actorId, CampaignsPermissions.Send);
        var schedule = SeedPaused(creatorCanViewAll: false);
        schedule.Resume();

        var result = await ResumeAsync(schedule);

        Assert.Equal(CampaignScheduleErrors.NotPaused, result.Error);
        Assert.Null(schedule.RunAsUserId);
        Assert.Empty(_schedules.Handovers);
    }

    private CampaignSchedule SeedPaused(bool creatorCanViewAll)
    {
        var schedule = CampaignSchedule
            .CreateRecurring(
                _tenantId,
                Guid.NewGuid(),
                DateTime.UtcNow.AddHours(1),
                RecurrenceFrequency.Daily,
                null,
                [],
                createdByUserId: _runAsId,
                creatorCanViewAllCustomers: creatorCanViewAll
            )
            .Value;
        schedule.Pause();
        _schedules.Schedules.Add(schedule);
        return schedule;
    }

    private Task<Result<CampaignScheduleResponse>> ResumeAsync(CampaignSchedule schedule) =>
        SetScheduleStateHandler.Handle(
            new SetScheduleStateCommand(_tenantId, schedule.Id, ScheduleAction.Resume, _actorId),
            _schedules,
            _permissions,
            _unitOfWork,
            CancellationToken.None
        );
}
