using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class CampaignRunTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Campaign = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static RunRecipientDraft Email(string? email) =>
        new(Guid.NewGuid().ToString("N"), CampaignChannel.Email, email, null);

    private static CampaignRun Start(params RunRecipientDraft[] units) =>
        CampaignRun.Start(Tenant, Campaign, User, "Manual", units).Value;

    [Fact]
    public void Start_materializes_one_unit_per_draft_and_starts_dispatching()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));

        Assert.Equal(2, run.RecipientCount);
        Assert.Equal(CampaignRunStatus.Dispatching, run.Status);
        Assert.All(run.Recipients, r => Assert.Equal(DispatchState.Pending, r.State));
    }

    [Fact]
    public void All_skipped_at_materialize_closes_Completed_not_Failed()
    {
        // Regresión: un run 100% Skipped (sin destino / opt-out) NO es un fallo del run.
        var run = Start(Email(null), Email(null));

        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(2, run.CounterSkipped);
        Assert.NotNull(run.FinishedAtUtc);
    }

    [Fact]
    public void All_delivered_closes_Completed()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        foreach (var r in run.Recipients.ToList())
            run.ApplyResult(r.DispatchId, DispatchOutcome.Delivered, null, null);

        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(2, run.CounterDelivered);
    }

    [Fact]
    public void Some_failed_some_delivered_closes_PartiallyFailed()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        var units = run.Recipients.ToList();
        run.ApplyResult(units[0].DispatchId, DispatchOutcome.Delivered, null, null);
        run.ApplyResult(units[1].DispatchId, DispatchOutcome.Failed, null, "bounce");

        Assert.Equal(CampaignRunStatus.PartiallyFailed, run.Status);
        Assert.Equal(1, run.CounterDelivered);
        Assert.Equal(1, run.CounterFailed);
    }

    [Fact]
    public void All_failed_closes_Failed()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        foreach (var r in run.Recipients.ToList())
            run.ApplyResult(r.DispatchId, DispatchOutcome.Failed, null, "err");

        Assert.Equal(CampaignRunStatus.Failed, run.Status);
        Assert.Equal(2, run.CounterFailed);
    }

    [Fact]
    public void Closed_run_returns_true_only_on_the_transition()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        var units = run.Recipients.ToList();

        var first = run.ApplyResult(units[0].DispatchId, DispatchOutcome.Delivered, null, null);
        Assert.False(first.Value); // aún falta una unidad

        var second = run.ApplyResult(units[1].DispatchId, DispatchOutcome.Delivered, null, null);
        Assert.True(second.Value); // esta llamada cerró el run
    }

    [Fact]
    public void Late_DLR_reconciles_Accepted_to_Delivered_without_reopening()
    {
        var run = Start(Email("a@x.com"));
        var unit = run.Recipients.Single();

        run.ApplyResult(unit.DispatchId, DispatchOutcome.Accepted, null, null);
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(1, run.CounterAccepted);

        // DLR tardío: Accepted → Delivered ajusta contadores, el run sigue Completed (no se reabre).
        run.ApplyResult(unit.DispatchId, DispatchOutcome.Delivered, "provider-123", null);
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(0, run.CounterAccepted);
        Assert.Equal(1, run.CounterDelivered);
    }

    [Fact]
    public void ApplyResultForRecipient_matches_by_recipient_id()
    {
        var run = Start(Email("a@x.com"));
        var unit = run.Recipients.Single();

        var result = run.ApplyResultForRecipient(unit.Id, DispatchOutcome.Delivered, null, null);
        Assert.True(result.IsSuccess);
        Assert.Equal(DispatchState.Delivered, unit.State);
    }

    [Fact]
    public void ApplyResult_unknown_dispatch_id_fails()
    {
        var run = Start(Email("a@x.com"));
        var result = run.ApplyResult("does-not-exist", DispatchOutcome.Delivered, null, null);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void StartScheduled_SameScheduleAndSlot_ProducesSameRunId()
    {
        var scheduleId = Guid.NewGuid();
        var slot = new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Utc);

        var run1 = CampaignRun.StartScheduled(Tenant, Campaign, User, scheduleId, slot, [Email("a@x.com")]).Value;
        // Mismo slot leído de SQL (Kind Unspecified) y otras unidades: mismo Id.
        var run2 = CampaignRun
            .StartScheduled(
                Tenant,
                Campaign,
                User,
                scheduleId,
                DateTime.SpecifyKind(slot, DateTimeKind.Unspecified),
                [Email("b@x.com"), Email("c@x.com")]
            )
            .Value;

        Assert.Equal(run1.Id, run2.Id);
        Assert.Equal(scheduleId, run1.ScheduleId);
        Assert.Equal(slot, run1.ScheduledSlotUtc);
        Assert.Equal("Scheduled", run1.TriggerKind);
        Assert.NotEqual(run1.Id, CampaignRun.StartScheduled(Tenant, Campaign, User, scheduleId, slot.AddMinutes(5), [Email("a@x.com")]).Value.Id);
    }

    [Fact]
    public void ExpireStaleUnits_DispatchedUnits_BecomeUnknownAndCloseRunOnce()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"), Email("c@x.com"));
        foreach (var r in run.Recipients.ToList())
            run.MarkDispatched(r.Id);
        run.ApplyResult(run.Recipients.First().DispatchId, DispatchOutcome.Delivered, null, null);

        var closed = run.ExpireStaleUnits(DateTime.UtcNow);

        Assert.True(closed);
        Assert.Equal(2, run.CounterUnknown);
        Assert.Equal(CampaignRunStatus.PartiallyFailed, run.Status);
        Assert.NotNull(run.FinishedAtUtc);
        Assert.All(run.Recipients.Where(r => r.State == DispatchState.Unknown), r => Assert.Equal("stale_timeout", r.Reason));
        Assert.False(run.ExpireStaleUnits(DateTime.UtcNow));
    }

    [Fact]
    public void ApplyResult_LateUnknown_DoesNotOverwriteAcceptedOrSkipped()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        var units = run.Recipients.ToList();
        run.ApplyResult(units[0].DispatchId, DispatchOutcome.Accepted, "p-1", null);
        run.ApplyResult(units[1].DispatchId, DispatchOutcome.Skipped, null, "opt_out");

        run.ApplyResult(units[0].DispatchId, DispatchOutcome.Unknown, null, "late");
        run.ApplyResult(units[1].DispatchId, DispatchOutcome.Unknown, null, "late");

        Assert.Equal(DispatchState.Accepted, units[0].State);
        Assert.Equal(DispatchState.Skipped, units[1].State);
        Assert.Equal(0, run.CounterUnknown);
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
    }

    [Fact]
    public void ApplyResult_UnknownResolvedAfterClose_RecalculatesStatusAndCounters()
    {
        var run = Start(Email("a@x.com"), Email("b@x.com"));
        var units = run.Recipients.ToList();
        run.ApplyResult(units[0].DispatchId, DispatchOutcome.Delivered, null, null);
        run.ApplyResult(units[1].DispatchId, DispatchOutcome.Unknown, null, "outcome_unknown");
        Assert.Equal(CampaignRunStatus.PartiallyFailed, run.Status);
        var finishedAt = run.FinishedAtUtc;

        // D-CMP-4: la conciliación resuelve la unidad después del cierre.
        var closedNow = run.ApplyResult(units[1].DispatchId, DispatchOutcome.Accepted, "p-2", null);

        Assert.False(closedNow.Value); // no vuelve a publicar run.completed
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(0, run.CounterUnknown);
        Assert.Equal(1, run.CounterAccepted);
        Assert.Equal(finishedAt, run.FinishedAtUtc);
    }
}
