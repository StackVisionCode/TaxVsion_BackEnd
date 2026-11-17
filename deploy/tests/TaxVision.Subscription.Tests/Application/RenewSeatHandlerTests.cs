using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.RenewSeat;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>La renovación gratis del soporte también deja el asiento terminando junto con el plan (D-A).</summary>
public sealed class RenewSeatHandlerTests
{
    [Fact]
    public async Task A_support_renewal_ends_with_the_plan()
    {
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        var seat = SubscriptionSeat
            .Purchase(
                tenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                nowUtc
            )
            .Value;
        seat.Activate(nowUtc.AddDays(-25), nowUtc.AddDays(5), Guid.Empty, nowUtc);
        var plan = TestSubscriptions.Active(tenantId, nowUtc.AddDays(-15), nowUtc.AddDays(15));
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);

        var result = await RenewSeatHandler.Handle(
            new RenewSeatCommand(tenantId, seat.Id, Guid.NewGuid()),
            seats,
            new FakeSubscriptionRepo(plan),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(plan.CurrentPeriodEndUtc, seat.CurrentPeriodEndUtc);
    }
}
