using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Trips;

public sealed class Trip : TenantEntity
{
    private Trip() { }

    public Guid CustomerId { get; private set; }
    public Guid ClientTripId { get; private set; }
    public Guid VehicleId { get; private set; }
    public string VehiclePlate { get; private set; } = string.Empty;
    public TripStatus Status { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public DateTime LastObservedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public long Revision { get; private set; }
    public long LastPointSequence { get; private set; }
    public DateTime LastPointAtUtc { get; private set; }
    public double LastLatitude { get; private set; }
    public double LastLongitude { get; private set; }
    public bool HasDistanceAnchor { get; private set; }
    public double DistanceMeters { get; private set; }
    public double ActiveDurationSeconds { get; private set; }
    public double MaxSpeedMetersPerSecond { get; private set; }
    public Guid? MileageRateId { get; private set; }
    public decimal? RatePerMile { get; private set; }
    public string? Currency { get; private set; }
    public decimal? EstimatedAmount { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Trip> Start(
        Guid tenant,
        Guid customer,
        Guid clientId,
        Guid vehicle,
        string plate,
        string purpose,
        string? notes,
        DateTime atUtc,
        GpsSample point,
        DateTime nowUtc
    )
    {
        if (
            tenant == Guid.Empty
            || customer == Guid.Empty
            || clientId == Guid.Empty
            || vehicle == Guid.Empty
            || !TripPurpose.IsValid(purpose)
            || notes?.Length > 2000
            || !ValidTime(atUtc, nowUtc)
            || !point.IsValid()
            || point.Sequence != 0
            || point.ObservedAtUtc != atUtc
        )
            return Result.Failure<Trip>(TripErrors.Invalid);
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            CustomerId = customer,
            ClientTripId = clientId,
            VehicleId = vehicle,
            VehiclePlate = plate,
            Purpose = purpose,
            Notes = notes,
            Status = TripStatus.InProgress,
            StartedAtUtc = atUtc,
            LastObservedAtUtc = atUtc,
            LastPointAtUtc = atUtc,
            LastLatitude = point.Latitude,
            LastLongitude = point.Longitude,
            HasDistanceAnchor = true,
            Revision = 1,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            MaxSpeedMetersPerSecond = point.SpeedMetersPerSecond ?? 0,
        };
        trip.SetTenant(tenant);
        return Result.Success(trip);
    }

    public Result Synchronize(long sequence, DateTime atUtc, IReadOnlyList<GpsSample> points, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status != TripStatus.InProgress)
            return Result.Failure(TripErrors.State);
        var valid = ValidatePoints(points, atUtc);
        if (valid.IsFailure)
            return valid;
        Append(points);
        Observe(atUtc, nowUtc);
        Revision = sequence;
        return Result.Success();
    }

    public Result Pause(long sequence, DateTime atUtc, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status != TripStatus.InProgress)
            return Result.Failure(TripErrors.State);
        Observe(atUtc, nowUtc);
        Status = TripStatus.Paused;
        HasDistanceAnchor = false;
        Revision = sequence;
        return Result.Success();
    }

    public Result Resume(long sequence, DateTime atUtc, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status != TripStatus.Paused)
            return Result.Failure(TripErrors.State);
        Observe(atUtc, nowUtc);
        Status = TripStatus.InProgress;
        HasDistanceAnchor = false;
        Revision = sequence;
        return Result.Success();
    }

    public Result Complete(long sequence, DateTime atUtc, IReadOnlyList<GpsSample> points, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status is not (TripStatus.InProgress or TripStatus.Paused))
            return Result.Failure(TripErrors.State);
        if (Status == TripStatus.Paused && points.Count > 0)
            return Result.Failure(TripErrors.State);
        var valid = ValidatePoints(points, atUtc);
        if (valid.IsFailure)
            return valid;
        Append(points);
        Observe(atUtc, nowUtc);
        Status = TripStatus.Completed;
        EndedAtUtc = atUtc;
        Revision = sequence;
        return Result.Success();
    }

    public Result Cancel(long sequence, DateTime atUtc, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status is not (TripStatus.InProgress or TripStatus.Paused))
            return Result.Failure(TripErrors.State);
        Observe(atUtc, nowUtc);
        Status = TripStatus.Cancelled;
        EndedAtUtc = atUtc;
        Revision = sequence;
        return Result.Success();
    }

    public Result Edit(long sequence, string purpose, string? notes, DateTime atUtc, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (!TripPurpose.IsValid(purpose) || notes?.Length > 2000)
            return Result.Failure(TripErrors.Invalid);
        Purpose = purpose;
        Notes = notes;
        UpdatedAtUtc = nowUtc;
        Revision = sequence;
        return Result.Success();
    }

    public Result Delete(long sequence, DateTime atUtc, DateTime nowUtc)
    {
        var check = Check(sequence, atUtc, nowUtc);
        if (check.IsFailure)
            return check;
        if (Status is TripStatus.InProgress or TripStatus.Paused)
            return Result.Failure(TripErrors.State);
        DeletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        Revision = sequence;
        return Result.Success();
    }

    public void ApplyRate(MileageRate? rate)
    {
        MileageRateId = rate?.Id;
        RatePerMile = rate?.RatePerMile;
        Currency = rate?.Currency;
        EstimatedAmount = rate is null
            ? null
            : decimal.Round((decimal)(DistanceMeters / 1609.344) * rate.RatePerMile, 2, MidpointRounding.AwayFromZero);
    }

    private Result Check(long sequence, DateTime atUtc, DateTime nowUtc)
    {
        if (DeletedAtUtc.HasValue)
            return Result.Failure(TripErrors.NotFound);
        if (sequence != Revision + 1)
            return Result.Failure(TripErrors.Sequence);
        return !ValidTime(atUtc, nowUtc) || atUtc < LastObservedAtUtc
            ? Result.Failure(TripErrors.Invalid)
            : Result.Success();
    }

    private Result ValidatePoints(IReadOnlyList<GpsSample> points, DateTime atUtc)
    {
        if (points.Count > 512)
            return Result.Failure(TripErrors.Invalid);
        var last = LastPointAtUtc;
        var next = LastPointSequence + 1;
        foreach (var point in points)
        {
            if (
                !point.IsValid()
                || point.Sequence != next
                || point.ObservedAtUtc < last
                || point.ObservedAtUtc < LastObservedAtUtc
                || point.ObservedAtUtc > atUtc
            )
                return Result.Failure(TripErrors.Invalid);
            last = point.ObservedAtUtc;
            next++;
        }
        return Result.Success();
    }

    private void Append(IReadOnlyList<GpsSample> points)
    {
        foreach (var point in points)
        {
            if (HasDistanceAnchor)
                DistanceMeters += GreatCircleMeters(LastLatitude, LastLongitude, point.Latitude, point.Longitude);
            LastLatitude = point.Latitude;
            LastLongitude = point.Longitude;
            LastPointAtUtc = point.ObservedAtUtc;
            LastPointSequence = point.Sequence;
            HasDistanceAnchor = true;
            MaxSpeedMetersPerSecond = Math.Max(MaxSpeedMetersPerSecond, point.SpeedMetersPerSecond ?? 0);
        }
    }

    private void Observe(DateTime atUtc, DateTime nowUtc)
    {
        if (Status == TripStatus.InProgress)
            ActiveDurationSeconds += (atUtc - LastObservedAtUtc).TotalSeconds;
        LastObservedAtUtc = atUtc;
        UpdatedAtUtc = nowUtc;
    }

    private static bool ValidTime(DateTime value, DateTime nowUtc) =>
        value.Kind == DateTimeKind.Utc && value >= DateTime.UnixEpoch && value <= nowUtc.AddMinutes(5);

    private static double GreatCircleMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a =
            Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2)
            + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371008.8 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
