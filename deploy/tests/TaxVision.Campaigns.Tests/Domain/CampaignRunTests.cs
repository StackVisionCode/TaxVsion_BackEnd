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
}
