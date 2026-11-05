using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class WalletReservationTests
{
    [Fact]
    public void Expire_OpenReservation_ReleasesEverything()
    {
        var reservation = ReservationBuilder.Open(Guid.NewGuid(), "campaign-run", Guid.NewGuid(), 500_000, 5);
        var now = DateTime.UtcNow;

        var result = reservation.Expire(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Equal(500_000, reservation.ReleasedMicros);
        Assert.Equal(0, reservation.ConsumedMicros);
        Assert.Equal(now, reservation.SettledAtUtc);
        Assert.True(reservation.Expire(now).IsFailure);
    }
}
