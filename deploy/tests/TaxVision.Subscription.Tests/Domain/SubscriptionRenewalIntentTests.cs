using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Intención de renovación self-service por hosted-checkout (Fase 4): el ciclo de vida Pending→Paid→
/// Provisioned (o →Failed), con transiciones idempotentes. Espejo de <c>SeatPurchaseIntentTests</c>.</summary>
public sealed class SubscriptionRenewalIntentTests
{
    private static SubscriptionRenewalIntent Pending() =>
        SubscriptionRenewalIntent
            .Create(Guid.NewGuid(), 4900, "USD", BillingCycle.Monthly, Guid.NewGuid(), DateTime.UtcNow)
            .Value;

    [Fact]
    public void Create_rejects_a_non_positive_amount()
    {
        var result = SubscriptionRenewalIntent.Create(
            Guid.NewGuid(),
            0,
            "USD",
            BillingCycle.Monthly,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SubscriptionRenewalIntent.NothingToCharge", result.Error.Code);
    }

    [Fact]
    public void Create_starts_pending_with_the_amount_and_currency()
    {
        var intent = Pending();

        Assert.Equal(SubscriptionRenewalIntentStatus.Pending, intent.Status);
        Assert.Equal(4900, intent.AmountCents);
        Assert.Equal("USD", intent.Currency);
    }

    [Fact]
    public void Full_happy_path_attach_pay_provision()
    {
        var intent = Pending();

        Assert.True(intent.AttachCheckout(Guid.NewGuid(), "https://pay/x", DateTime.UtcNow).IsSuccess);
        Assert.True(intent.MarkPaid(DateTime.UtcNow).IsSuccess);
        Assert.True(intent.MarkProvisioned(DateTime.UtcNow).IsSuccess);
        Assert.Equal(SubscriptionRenewalIntentStatus.Provisioned, intent.Status);
    }

    [Fact]
    public void MarkPaid_is_idempotent_once_provisioned()
    {
        var intent = Pending();
        intent.MarkPaid(DateTime.UtcNow);
        intent.MarkProvisioned(DateTime.UtcNow);

        Assert.True(intent.MarkPaid(DateTime.UtcNow).IsSuccess); // no-op, no throw
        Assert.Equal(SubscriptionRenewalIntentStatus.Provisioned, intent.Status);
    }

    [Fact]
    public void Cannot_fail_an_already_provisioned_intent()
    {
        var intent = Pending();
        intent.MarkPaid(DateTime.UtcNow);
        intent.MarkProvisioned(DateTime.UtcNow);

        var result = intent.MarkFailed("card_declined", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SubscriptionRenewalIntent.AlreadyProvisioned", result.Error.Code);
    }
}
