using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Cancel/Expire sobre la intención de compra de add-on (hosted-checkout).</summary>
public sealed class AddOnPurchaseIntentCancelExpireTests
{
    private static AddOnDefinition Definition() =>
        AddOnDefinition
            .Create(
                AddOnCode.Create("extra-storage").Value,
                "Extra storage",
                "100GB of extra storage",
                "storage",
                allowMultipleInstances: true,
                [BillingCycle.Monthly],
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    private static AddOnPurchaseIntent Pending() =>
        AddOnPurchaseIntent
            .Create(
                Guid.NewGuid(),
                Definition(),
                quantity: 1,
                autoRenew: true,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 1000,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Cancel_moves_pending_to_cancelled_with_actor()
    {
        var intent = Pending();
        var actor = Guid.NewGuid();

        var result = intent.Cancel(actor, DateTime.UtcNow, "user closed the window");

        Assert.True(result.IsSuccess);
        Assert.Equal(AddOnPurchaseIntentStatus.Cancelled, intent.Status);
        Assert.Equal(actor, intent.CancelledByUserId);
    }

    [Fact]
    public void Cancel_rejects_a_paid_intent()
    {
        var intent = Pending();
        intent.MarkPaid(DateTime.UtcNow);

        var result = intent.Cancel(Guid.NewGuid(), DateTime.UtcNow, null);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOnPurchaseIntent.AlreadyPaid", result.Error.Code);
    }

    [Fact]
    public void Cancel_is_idempotent_from_cancelled_or_failed()
    {
        var intent = Pending();
        intent.Cancel(Guid.NewGuid(), DateTime.UtcNow, null);
        Assert.True(intent.Cancel(Guid.NewGuid(), DateTime.UtcNow, null).IsSuccess);

        var failed = Pending();
        failed.MarkFailed("provider error", DateTime.UtcNow);
        Assert.True(failed.Cancel(Guid.NewGuid(), DateTime.UtcNow, null).IsSuccess);
    }

    [Fact]
    public void Expire_closes_an_elapsed_pending()
    {
        var intent = Pending();
        intent.AttachCheckout(Guid.NewGuid(), "https://x", DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);

        intent.Expire(DateTime.UtcNow);

        Assert.Equal(AddOnPurchaseIntentStatus.Expired, intent.Status);
    }

    [Fact]
    public void Expire_leaves_an_open_session_untouched()
    {
        var intent = Pending();
        intent.AttachCheckout(Guid.NewGuid(), "https://x", DateTime.UtcNow.AddHours(1), DateTime.UtcNow);

        intent.Expire(DateTime.UtcNow);

        Assert.Equal(AddOnPurchaseIntentStatus.Pending, intent.Status);
    }
}
