using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// C5 — asientos y add-ons se seguían cobrando con la base caída. Se cobra mientras el tenant recibe algo:
/// la misma lista de estados que dan entitlements.
/// </summary>
public sealed class ExtraBillingTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trialing)]
    // PastDue y GracePeriod todavía dan acceso: el tenant sigue trabajando, así que el extra se cobra.
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public void While_the_base_still_gives_access_the_extra_renews(SubscriptionStatus status)
    {
        Assert.Equal(ExtraBillingDecision.Renew, ExtraBilling.Decide(status));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.Draft)]
    public void With_the_base_down_but_recoverable_the_extra_waits(SubscriptionStatus status)
    {
        Assert.Equal(ExtraBillingDecision.Pause, ExtraBilling.Decide(status));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public void With_the_base_finished_the_extra_is_cancelled(SubscriptionStatus status)
    {
        Assert.Equal(ExtraBillingDecision.Cancel, ExtraBilling.Decide(status));
    }
}
