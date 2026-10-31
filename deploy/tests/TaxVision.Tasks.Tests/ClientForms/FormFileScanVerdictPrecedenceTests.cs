using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormFileScanVerdictPrecedenceTests
{
    private static readonly DateTime First = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = First.AddMinutes(5);

    [Theory]
    [InlineData(FormFileVerdictStatus.Infected, FormFileVerdictStatus.Blocked)]
    [InlineData(FormFileVerdictStatus.Blocked, FormFileVerdictStatus.ScanFailed)]
    [InlineData(FormFileVerdictStatus.ScanFailed, FormFileVerdictStatus.Infected)]
    [InlineData(FormFileVerdictStatus.Infected, FormFileVerdictStatus.Available)]
    [InlineData(FormFileVerdictStatus.Available, FormFileVerdictStatus.Available)]
    public void Newer_event_that_the_table_ignores_does_not_replace_the_state(
        FormFileVerdictStatus current,
        FormFileVerdictStatus incoming
    )
    {
        var verdict = Verdict(current);

        Assert.False(Apply(verdict, incoming, Later));
        Assert.Equal(current, verdict.Status);
        Assert.Equal(First, verdict.LastEventOccurredAtUtc);
    }

    [Theory]
    [InlineData(FormFileVerdictStatus.Infected)]
    [InlineData(FormFileVerdictStatus.Blocked)]
    [InlineData(FormFileVerdictStatus.ScanFailed)]
    public void Newer_negative_rescan_replaces_available(FormFileVerdictStatus incoming)
    {
        var verdict = Verdict(FormFileVerdictStatus.Available);

        Assert.True(Apply(verdict, incoming, Later));
        Assert.Equal(incoming, verdict.Status);
    }

    [Fact]
    public void Negative_rescan_with_the_same_timestamp_does_not_replace_available()
    {
        var verdict = Verdict(FormFileVerdictStatus.Available);

        Assert.False(Apply(verdict, FormFileVerdictStatus.Infected, First));
        Assert.Equal(FormFileVerdictStatus.Available, verdict.Status);
    }

    [Theory]
    [InlineData(FormFileVerdictStatus.Available)]
    [InlineData(FormFileVerdictStatus.Infected)]
    public void Deleted_applies_over_any_state_and_is_terminal(FormFileVerdictStatus current)
    {
        var verdict = Verdict(current);

        Assert.True(Apply(verdict, FormFileVerdictStatus.Deleted, Later));
        Assert.False(Apply(verdict, FormFileVerdictStatus.Available, Later.AddMinutes(1)));
        Assert.Equal(FormFileVerdictStatus.Deleted, verdict.Status);
    }

    // T-20: la revisión humana se cierra con un veredicto final, nunca con otra revisión.
    [Theory]
    [InlineData(FormFileVerdictStatus.Available)]
    [InlineData(FormFileVerdictStatus.Blocked)]
    public void A_final_verdict_replaces_pending_review(FormFileVerdictStatus incoming)
    {
        var verdict = Verdict(FormFileVerdictStatus.PendingReview);

        Assert.False(Apply(verdict, FormFileVerdictStatus.PendingReview, Later));
        Assert.True(Apply(verdict, incoming, Later));
        Assert.Equal(incoming, verdict.Status);
    }

    [Fact]
    public void Older_event_is_always_ignored()
    {
        var verdict = Verdict(FormFileVerdictStatus.Available);

        Assert.False(Apply(verdict, FormFileVerdictStatus.Deleted, First.AddMinutes(-1)));
        Assert.Equal(FormFileVerdictStatus.Available, verdict.Status);
    }

    private static FormFileScanVerdict Verdict(FormFileVerdictStatus status) =>
        FormFileScanVerdict.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
            "Customer",
            Guid.NewGuid(),
            "ClientForms",
            "application/pdf",
            10,
            "return.pdf",
            First,
            First
        );

    private static bool Apply(FormFileScanVerdict verdict, FormFileVerdictStatus status, DateTime occurredAtUtc) =>
        verdict.Apply(status, null, null, "ClientForms", null, null, null, occurredAtUtc, occurredAtUtc);
}
