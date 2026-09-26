using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Comprar VARIOS asientos de una vez creaba todos con el mismo objeto de precio: EF Core no puede compartir
/// una instancia de owned type entre filas, así que rastreaba una sola y las demás se insertaban con
/// <c>UnitPriceAmount</c> NULL — el guardado reventaba entero y el tenant quedaba cobrado y sin asientos.
/// Comprar uno solo nunca lo destapó. Cada asiento tiene su propia copia del precio.
/// </summary>
public sealed class SeatUnitPriceOwnershipTests
{
    [Fact]
    public void Seats_bought_together_do_not_share_the_same_price_instance()
    {
        var price = Money.Create(15.00m, "USD").Value;
        var tenantId = Guid.NewGuid();

        var seats = Enumerable.Range(0, 3).Select(_ => Purchase(tenantId, price)).ToArray();

        Assert.All(seats, seat => Assert.Equal(15.00m, seat.UnitPrice.Amount));
        Assert.All(seats, seat => Assert.Equal("USD", seat.UnitPrice.Currency));
        Assert.Distinct(seats.Select(seat => (object)seat.UnitPrice), ReferenceComparer.Instance);
        // Tampoco comparten la instancia con el precio de origen.
        Assert.All(seats, seat => Assert.NotSame(price, seat.UnitPrice));
    }

    private static SubscriptionSeat Purchase(Guid tenantId, Money price) =>
        SubscriptionSeat
            .Purchase(
                tenantId,
                SeatType.Standard,
                SeatSourceType.Plan,
                sourceReferenceId: null,
                price,
                BillingCycle.Monthly,
                autoRenew: true,
                actorUserId: Guid.Empty,
                nowUtc: DateTime.UtcNow
            )
            .Value;

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
