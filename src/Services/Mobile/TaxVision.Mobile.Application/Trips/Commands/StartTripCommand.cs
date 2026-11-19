using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Commands;

public sealed record StartTripCommand(Guid TenantId, Guid CustomerId, StartTripInput Input)
{
    public override string ToString() =>
        $"StartTripCommand {{ TenantId = {TenantId}, CustomerId = {CustomerId}, ClientTripId = {Input.ClientTripId} }}";
}

public static class StartTripHandler
{
    public static async Task<Result<TripResponse>> Handle(
        StartTripCommand cmd,
        ITripRepository trips,
        IVehicleRepository vehicles,
        IUnitOfWork work,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (cmd.Input.StartPoint is null)
            return Result.Failure<TripResponse>(TripErrors.Invalid);
        var hash = Hash(cmd.Input);
        var existing = await trips.FindClientAsync(cmd.TenantId, cmd.CustomerId, cmd.Input.ClientTripId, ct);
        if (existing is not null)
            return await Replay(existing, hash, trips, ct);
        var vehicle = await vehicles.FindAsync(cmd.TenantId, cmd.CustomerId, cmd.Input.VehicleId, ct);
        if (vehicle is null)
            return Result.Failure<TripResponse>(TripErrors.NotFound);
        var now = clock.GetUtcNow().UtcDateTime;
        var created = Create(cmd.TenantId, cmd.CustomerId, cmd.Input, vehicle.Id, vehicle.Details.Plate, now);
        if (created.IsFailure)
            return Result.Failure<TripResponse>(created.Error);
        trips.Add(created.Value);
        trips.AddPoint(TripPoint.From(created.Value, cmd.Input.StartPoint.ToSample()));
        trips.AddOperation(TripOperation.Create(created.Value, "Start", hash, cmd.Input.StartedAt.UtcDateTime, now));
        await work.SaveChangesAsync(ct);
        return Result.Success(TripResponse.From(created.Value));
    }

    private static string Hash(StartTripInput input)
    {
        return TripValidation.Hash(
            "Start",
            new
            {
                input.ClientTripId,
                input.VehicleId,
                input.Purpose,
                StartedAt = input.StartedAt.UtcDateTime,
                StartPoint = input.StartPoint!.ToSample(),
                input.Notes,
            }
        );
    }

    private static Result<Trip> Create(
        Guid tenant,
        Guid customer,
        StartTripInput input,
        Guid vehicleId,
        string plate,
        DateTime now
    )
    {
        return Trip.Start(
            tenant,
            customer,
            input.ClientTripId,
            vehicleId,
            plate,
            input.Purpose,
            input.Notes,
            input.StartedAt.UtcDateTime,
            input.StartPoint!.ToSample(),
            now
        );
    }

    private static async Task<Result<TripResponse>> Replay(
        Trip trip,
        string hash,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        if (trip.DeletedAtUtc.HasValue)
            return Result.Failure<TripResponse>(TripErrors.NotFound);
        var operation = await trips.OperationAsync(trip.TenantId, trip.CustomerId, trip.Id, 1, ct);
        return operation?.PayloadHash == hash
            ? Result.Success(TripResponse.From(trip, true))
            : Result.Failure<TripResponse>(TripErrors.Replay);
    }
}
