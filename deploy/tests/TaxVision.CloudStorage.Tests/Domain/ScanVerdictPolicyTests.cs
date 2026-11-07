using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>CST-9. De los hallazgos del escaneo sale el destino del archivo.</summary>
public sealed class ScanVerdictPolicyTests
{
    [Fact]
    public void Content_that_is_not_allowed_is_discarded_and_wins_over_the_rest()
    {
        var decision = ScanVerdictPolicy.Decide(new ScanFindings(false, true, ScanModeration.PolicyViolation));

        Assert.Equal(
            new ScanDecision(
                ScanOutcome.ContentRejected,
                ScanTarget.Discard,
                ScanQuotaEffect.Release,
                false,
                "rejected"
            ),
            decision
        );
    }

    [Fact]
    public void An_infected_file_goes_to_quarantine_and_frees_its_quota()
    {
        var decision = ScanVerdictPolicy.Decide(new ScanFindings(true, Infected: true));

        Assert.Equal(
            new ScanDecision(ScanOutcome.Infected, ScanTarget.Quarantine, ScanQuotaEffect.Release, false, "infected"),
            decision
        );
    }

    [Fact]
    public void A_policy_violation_is_kept_as_evidence()
    {
        var decision = ScanVerdictPolicy.Decide(new ScanFindings(true, Moderation: ScanModeration.PolicyViolation));

        Assert.Equal(
            new ScanDecision(
                ScanOutcome.BlockedByPolicy,
                ScanTarget.Quarantine,
                ScanQuotaEffect.Release,
                true,
                "blocked-by-policy"
            ),
            decision
        );
    }

    [Fact]
    public void An_uncertain_verdict_waits_for_a_human_in_quarantine()
    {
        var decision = ScanVerdictPolicy.Decide(new ScanFindings(true, Moderation: ScanModeration.Uncertain));

        Assert.Equal(
            new ScanDecision(
                ScanOutcome.PendingReview,
                ScanTarget.Quarantine,
                ScanQuotaEffect.Release,
                false,
                "pending-review"
            ),
            decision
        );
    }

    [Fact]
    public void A_clean_file_goes_to_the_main_bucket_and_counts()
    {
        var decision = ScanVerdictPolicy.Decide(new ScanFindings(true));

        Assert.Equal(
            new ScanDecision(ScanOutcome.Clean, ScanTarget.Main, ScanQuotaEffect.Commit, false, "clean"),
            decision
        );
    }
}
