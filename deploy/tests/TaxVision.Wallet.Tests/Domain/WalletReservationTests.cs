using TaxVision.Wallet.Domain.Pricing;
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

    [Fact]
    public void Open_WithTrace_KeepsDescriptionUnitsPerChannelAndRequester()
    {
        var userId = Guid.NewGuid();
        var trace = new ReservationTrace(" Spring promo ", new ReservationChannelUnits(10, 3, 0, 1), userId);

        var reservation = OpenWith(trace);

        Assert.Equal("Spring promo", reservation.Description);
        Assert.Equal(10, reservation.EmailUnits);
        Assert.Equal(3, reservation.SmsUnits);
        Assert.Equal(0, reservation.PushUnits);
        Assert.Equal(1, reservation.WhatsAppUnits);
        Assert.Equal(userId, reservation.RequestedByUserId);
        Assert.Null(reservation.ConsumedUnits);
        Assert.Equal(
            [(PriceChannel.Email, 10), (PriceChannel.Sms, 3), (PriceChannel.WhatsApp, 1)],
            reservation.ChannelUnits()
        );
    }

    [Fact]
    public void Open_LongOrBlankDescription_IsCutToTheLimitOrDropped()
    {
        var longText = new string('x', WalletReservation.MaxDescriptionLength + 50);

        var cut = OpenWith(new ReservationTrace(longText, ReservationChannelUnits.None, null));
        var blank = OpenWith(new ReservationTrace("   ", ReservationChannelUnits.None, null));

        Assert.Equal(WalletReservation.MaxDescriptionLength, cut.Description!.Length);
        Assert.Null(blank.Description);
    }

    [Fact]
    public void Settle_KeepsTheConsumedUnits()
    {
        var reservation = ReservationBuilder.Open(Guid.NewGuid(), "campaign-run", Guid.NewGuid(), 500_000, 5);

        var result = reservation.Settle(Usd(200_000), Usd(300_000), consumedUnits: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, reservation.ConsumedUnits);
    }

    [Fact]
    public void Expire_RecordsZeroConsumedUnits()
    {
        var reservation = ReservationBuilder.Open(Guid.NewGuid(), "campaign-run", Guid.NewGuid(), 500_000, 5);

        reservation.Expire(DateTime.UtcNow);

        Assert.Equal(0, reservation.ConsumedUnits);
    }

    private static WalletReservation OpenWith(ReservationTrace trace) =>
        WalletReservation
            .Open(
                Guid.NewGuid(),
                ReservationReference.Create("campaign-run", Guid.NewGuid()).Value,
                Usd(500_000),
                14,
                1,
                trace
            )
            .Value;
}
