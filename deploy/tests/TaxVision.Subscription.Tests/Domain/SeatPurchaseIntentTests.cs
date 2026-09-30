using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>La intención de compra por hosted-checkout: máquina de estados Pending → Paid → Provisioned
/// (o → Failed), idempotente, que nunca revierte un aprovisionamiento ya hecho.</summary>
public sealed class SeatPurchaseIntentTests
{
    private static SeatPurchaseIntent CreatePending() =>
        SeatPurchaseIntent
            .Create(
                Guid.NewGuid(),
                SeatType.Standard,
                quantity: 2,
                autoRenew: true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 3000,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Create_starts_pending_and_takes_currency_from_the_unit_price()
    {
        var intent = CreatePending();

        Assert.Equal(SeatPurchaseIntentStatus.Pending, intent.Status);
        Assert.Equal("USD", intent.Currency);
        Assert.Equal(3000, intent.ProratedTotalCents);
    }

    [Fact]
    public void Create_rejects_a_zero_total_checkout()
    {
        var result = SeatPurchaseIntent.Create(
            Guid.NewGuid(),
            SeatType.Portal,
            quantity: 1,
            autoRenew: false,
            Money.Zero("USD"),
            BillingCycle.Monthly,
            proratedTotalCents: 0,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPurchaseIntent.NothingToCharge", result.Error.Code);
    }

    [Fact]
    public void AttachCheckout_records_the_payment_and_url_and_stays_pending()
    {
        var intent = CreatePending();
        var paymentId = Guid.NewGuid();

        var result = intent.AttachCheckout(
            paymentId,
            "https://checkout.example/abc",
            DateTime.UtcNow.AddHours(24),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, intent.SaaSPaymentId);
        Assert.Equal("https://checkout.example/abc", intent.CheckoutUrl);
        Assert.Equal(SeatPurchaseIntentStatus.Pending, intent.Status);
    }

    [Fact]
    public void MarkPaid_moves_pending_to_paid_and_is_idempotent()
    {
        var intent = CreatePending();
        var paidAt = DateTime.UtcNow;

        Assert.True(intent.MarkPaid(paidAt).IsSuccess);
        Assert.Equal(SeatPurchaseIntentStatus.Paid, intent.Status);
        Assert.Equal(paidAt, intent.PaidAtUtc);
        Assert.True(intent.MarkPaid(paidAt.AddMinutes(1)).IsSuccess); // idempotente
        Assert.Equal(paidAt, intent.PaidAtUtc);
    }

    [Fact]
    public void MarkProvisioned_requires_paid_and_is_idempotent()
    {
        var intent = CreatePending();

        Assert.True(intent.MarkProvisioned(DateTime.UtcNow).IsFailure); // aún Pending
        intent.MarkPaid(DateTime.UtcNow);
        Assert.True(intent.MarkProvisioned(DateTime.UtcNow).IsSuccess);
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, intent.Status);
        Assert.True(intent.MarkProvisioned(DateTime.UtcNow).IsSuccess); // idempotente
    }

    [Fact]
    public void MarkFailed_never_reverts_a_provisioned_intent()
    {
        var intent = CreatePending();
        intent.MarkPaid(DateTime.UtcNow);
        intent.MarkProvisioned(DateTime.UtcNow);

        var result = intent.MarkFailed("late failure", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPurchaseIntent.AlreadyProvisioned", result.Error.Code);
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, intent.Status);
    }
}
