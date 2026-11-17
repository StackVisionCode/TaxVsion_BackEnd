using TaxVision.Subscription.Application.Seats.Queries;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>La compra abierta dice con qué proveedor se empezó, para que la pantalla lo muestre y ofrezca el otro.</summary>
public sealed class CheckoutStatusProviderTests
{
    [Fact]
    public async Task The_open_seat_purchase_says_its_provider()
    {
        var nowUtc = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var intent = SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                1,
                autoRenew: true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                Money.Create(15m, "USD").Value,
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://www.sandbox.paypal.com/x", nowUtc.AddHours(1), nowUtc, "PayPal");

        var status = await GetSeatCheckoutStatusHandler.Handle(
            new GetSeatCheckoutStatusQuery(tenantId, intent.Id),
            new FakeSeatPurchaseIntentRepository(intent),
            CancellationToken.None
        );

        Assert.Equal("PayPal", status.Value.CheckoutProvider);
    }
}
