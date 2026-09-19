using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class CampaignScheduleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Campaign = Guid.NewGuid();

    [Fact]
    public void CreateRecurring_requires_positive_interval()
    {
        var result = CampaignSchedule.CreateRecurring(Tenant, Campaign, DateTime.UtcNow, 0, []);
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignScheduleErrors.IntervalInvalid, result.Error);
    }

    [Fact]
    public void OneTime_MarkFired_completes_and_clears_next()
    {
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, []).Value;
        var runId = Guid.NewGuid();

        schedule.MarkFired(runId, DateTime.UtcNow);

        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
        Assert.Null(schedule.NextFireAtUtc);
        Assert.Equal(runId, schedule.ActiveRunId);
    }

    [Fact]
    public void Recurring_MarkFired_coalesces_missed_slots_to_next_future()
    {
        // NextFire muy atrasado (servicio caído): al disparar, avanza al próximo slot FUTURO, no uno por cada perdido.
        var start = DateTime.UtcNow.AddMinutes(-100);
        var schedule = CampaignSchedule.CreateRecurring(Tenant, Campaign, start, 10, []).Value;

        var now = DateTime.UtcNow;
        schedule.MarkFired(Guid.NewGuid(), now);

        Assert.Equal(ScheduleStatus.Active, schedule.Status);
        Assert.NotNull(schedule.NextFireAtUtc);
        Assert.True(schedule.NextFireAtUtc > now, "next fire must be in the future (coalesced)");
    }

    [Fact]
    public void MarkFired_with_empty_runId_does_not_set_overlap_guard()
    {
        var schedule = CampaignSchedule.CreateRecurring(Tenant, Campaign, DateTime.UtcNow, 10, []).Value;
        schedule.MarkFired(Guid.Empty, DateTime.UtcNow);
        Assert.Null(schedule.ActiveRunId);
    }

    [Fact]
    public void ReleaseActiveRun_clears_only_matching_run()
    {
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, []).Value;
        var runId = Guid.NewGuid();
        schedule.MarkFired(runId, DateTime.UtcNow);

        schedule.ReleaseActiveRun(Guid.NewGuid()); // otro run: no-op
        Assert.Equal(runId, schedule.ActiveRunId);

        schedule.ReleaseActiveRun(runId);
        Assert.Null(schedule.ActiveRunId);
    }

    [Fact]
    public void IsClaimable_true_only_when_active_due_unleased_and_no_active_run()
    {
        var past = DateTime.UtcNow.AddMinutes(-1);
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, past, []).Value;
        Assert.True(schedule.IsClaimable(DateTime.UtcNow));

        schedule.Pause();
        Assert.False(schedule.IsClaimable(DateTime.UtcNow));
    }

    [Fact]
    public void Pause_resume_cancel_transitions()
    {
        var schedule = CampaignSchedule.CreateRecurring(Tenant, Campaign, DateTime.UtcNow, 10, []).Value;

        Assert.True(schedule.Pause().IsSuccess);
        Assert.True(schedule.Pause().IsFailure); // ya no está Active
        Assert.True(schedule.Resume().IsSuccess);
        Assert.True(schedule.Cancel().IsSuccess);
        Assert.Equal(ScheduleStatus.Cancelled, schedule.Status);
        Assert.Null(schedule.NextFireAtUtc);
    }

    [Fact]
    public void ContactListIds_roundtrips_from_csv()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var schedule = CampaignSchedule.CreateOneTime(Tenant, Campaign, DateTime.UtcNow, [a, b, a]).Value;
        var ids = schedule.ContactListIds();
        Assert.Equal(2, ids.Count); // deduped
        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
    }
}
