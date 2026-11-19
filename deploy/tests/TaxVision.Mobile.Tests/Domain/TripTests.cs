using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Tests.Domain;

public sealed class TripTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddHours(-1);

    private static Trip New() =>
        Trip.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ABC",
            "business",
            null,
            Start,
            new(0, Start, 40, -74, 5),
            Now
        ).Value;

    [Fact]
    public void Distance_is_calculated_from_points_and_paused_time_is_excluded()
    {
        var trip = New();
        Assert.True(
            trip.Synchronize(2, Start.AddMinutes(10), [new(1, Start.AddMinutes(10), 40.01, -74, 5, 10)], Now).IsSuccess
        );
        Assert.InRange(trip.DistanceMeters, 1100, 1120);
        Assert.True(trip.Pause(3, Start.AddMinutes(15), Now).IsSuccess);
        Assert.True(trip.Resume(4, Start.AddMinutes(30), Now).IsSuccess);
        Assert.True(trip.Complete(5, Start.AddMinutes(40), [new(2, Start.AddMinutes(40), 41, -74, 5)], Now).IsSuccess);
        Assert.Equal(1500, trip.ActiveDurationSeconds);
        Assert.InRange(trip.DistanceMeters, 1100, 1120);
        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.Equal(TripErrors.State, trip.Synchronize(6, Start.AddMinutes(45), [], Now).Error);
    }

    [Fact]
    public void Invalid_batch_does_not_partially_mutate_the_aggregate()
    {
        var trip = New();
        var result = trip.Synchronize(
            2,
            Start.AddMinutes(2),
            [new(1, Start.AddMinutes(1), 40.01, -74, 5), new(3, Start.AddMinutes(2), 40.02, -74, 5)],
            Now
        );
        Assert.True(result.IsFailure);
        Assert.Equal(1, trip.Revision);
        Assert.Equal(0, trip.DistanceMeters);
        Assert.Equal(0, trip.LastPointSequence);
    }

    [Fact]
    public void Out_of_order_or_reused_sequences_and_clock_regression_are_rejected()
    {
        var trip = New();
        Assert.Equal(TripErrors.Sequence, trip.Pause(3, Start.AddMinutes(1), Now).Error);
        Assert.True(trip.Pause(2, Start.AddMinutes(2), Now).IsSuccess);
        Assert.Equal(TripErrors.Invalid, trip.Resume(3, Start.AddMinutes(1), Now).Error);
        Assert.Equal(TripErrors.State, trip.Synchronize(3, Start.AddMinutes(3), [], Now).Error);
        Assert.Equal(TripErrors.Invalid, trip.Resume(3, Now.AddMinutes(6), Now).Error);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void Invalid_coordinates_are_rejected(double lat, double lon) =>
        Assert.False(new GpsSample(0, Start, lat, lon, 5).IsValid());

    [Fact]
    public void Cancellation_is_terminal_and_active_trip_cannot_be_deleted()
    {
        var trip = New();
        Assert.Equal(TripErrors.State, trip.Delete(2, Start.AddMinutes(1), Now).Error);
        Assert.True(trip.Cancel(2, Start.AddMinutes(1), Now).IsSuccess);
        Assert.Equal(TripErrors.State, trip.Resume(3, Start.AddMinutes(2), Now).Error);
        Assert.True(trip.Delete(3, Start.AddMinutes(2), Now).IsSuccess);
        Assert.Equal(TripErrors.NotFound, trip.Edit(4, "business", null, Start.AddMinutes(3), Now).Error);
    }

    [Fact]
    public void Estimates_use_explicit_rate_and_missing_configuration_stays_null()
    {
        var trip = New();
        trip.Synchronize(2, Start.AddMinutes(1), [new(1, Start.AddMinutes(1), 40.01, -74, 5)], Now);
        trip.Complete(3, Start.AddMinutes(2), [], Now);
        trip.ApplyRate(null);
        Assert.Null(trip.EstimatedAmount);
        var rate = MileageRate.Create(trip.TenantId, "business", DateOnly.FromDateTime(Now), 0.1234m, "USD", Now).Value;
        trip.ApplyRate(rate);
        Assert.Equal(rate.Id, trip.MileageRateId);
        Assert.Equal(
            decimal.Round(
                (decimal)(trip.DistanceMeters / 1609.344) * rate.RatePerMile,
                2,
                MidpointRounding.AwayFromZero
            ),
            trip.EstimatedAmount
        );
    }

    [Theory]
    [InlineData("unknown", 1, "USD")]
    [InlineData("business", -1, "USD")]
    [InlineData("business", 1, "usd")]
    public void Invalid_rates_are_rejected(string purpose, decimal rate, string currency) =>
        Assert.True(
            MileageRate.Create(Guid.NewGuid(), purpose, DateOnly.FromDateTime(Now), rate, currency, Now).IsFailure
        );

    [Fact]
    public void Rates_cannot_be_backdated() =>
        Assert.True(
            MileageRate
                .Create(Guid.NewGuid(), "business", DateOnly.FromDateTime(Now).AddDays(-1), 1, "USD", Now)
                .IsFailure
        );
}
