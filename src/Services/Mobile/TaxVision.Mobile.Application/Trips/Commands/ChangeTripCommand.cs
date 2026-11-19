using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Commands;

public sealed record ChangeTripCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid TripId,
    TripAction Action,
    long Sequence,
    DateTimeOffset ObservedAt,
    IReadOnlyList<GpsPointInput>? Points = null,
    string? Purpose = null,
    string? Notes = null
)
{
    public override string ToString() =>
        $"ChangeTripCommand {{ TenantId = {TenantId}, CustomerId = {CustomerId}, TripId = {TripId}, Action = {Action}, Sequence = {Sequence} }}";
}

public static class ChangeTripHandler
{
    public static async Task<Result<TripResponse>> Handle(
        ChangeTripCommand cmd,
        ITripRepository trips,
        IUnitOfWork work,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (cmd.Points?.Count > 512 || cmd.Points?.Any(p => p is null) == true)
            return Result.Failure<TripResponse>(TripErrors.Invalid);
        var trip = await trips.FindAsync(cmd.TenantId, cmd.CustomerId, cmd.TripId, ct);
        if (trip is null)
            return Result.Failure<TripResponse>(TripErrors.NotFound);
        var samples = cmd.Points?.Select(p => p.ToSample()).ToArray() ?? [];
        var hash = Hash(cmd, samples);
        var prior = await trips.OperationAsync(cmd.TenantId, cmd.CustomerId, cmd.TripId, cmd.Sequence, ct);
        if (prior is not null)
            return prior.PayloadHash == hash && (!trip.DeletedAtUtc.HasValue || cmd.Action == TripAction.Delete)
                ? Result.Success(TripResponse.From(trip, true))
                : Result.Failure<TripResponse>(TripErrors.Replay);
        return await Persist(trip, cmd, samples, hash, trips, work, clock.GetUtcNow().UtcDateTime, ct);
    }

    private static string Hash(ChangeTripCommand cmd, IReadOnlyList<GpsSample> samples)
    {
        return TripValidation.Hash(
            cmd.Action.ToString(),
            new
            {
                cmd.Sequence,
                ObservedAt = cmd.ObservedAt.UtcDateTime,
                Points = samples,
                cmd.Purpose,
                cmd.Notes,
            }
        );
    }

    private static async Task<Result<TripResponse>> Persist(
        Trip trip,
        ChangeTripCommand cmd,
        IReadOnlyList<GpsSample> samples,
        string hash,
        ITripRepository trips,
        IUnitOfWork work,
        DateTime now,
        CancellationToken ct
    )
    {
        var purposeChanged = cmd.Action == TripAction.Edit && cmd.Purpose != trip.Purpose;
        var changed = Apply(trip, cmd, samples, now);
        if (changed.IsFailure)
            return Result.Failure<TripResponse>(changed.Error);
        foreach (var sample in samples)
            trips.AddPoint(TripPoint.From(trip, sample));
        if (trip.Status == TripStatus.Completed && (cmd.Action == TripAction.Complete || purposeChanged))
            trip.ApplyRate(
                await trips.EffectiveRateAsync(cmd.TenantId, trip.Purpose, DateOnly.FromDateTime(trip.StartedAtUtc), ct)
            );
        trips.AddOperation(TripOperation.Create(trip, cmd.Action.ToString(), hash, cmd.ObservedAt.UtcDateTime, now));
        await work.SaveChangesAsync(ct);
        return Result.Success(TripResponse.From(trip));
    }

    private static Result Apply(Trip trip, ChangeTripCommand cmd, IReadOnlyList<GpsSample> points, DateTime now)
    {
        var at = cmd.ObservedAt.UtcDateTime;
        if (points.Count > 0 && cmd.Action is not (TripAction.Tracking or TripAction.Complete))
            return Result.Failure(TripErrors.Invalid);
        return cmd.Action switch
        {
            TripAction.Tracking => trip.Synchronize(cmd.Sequence, at, points, now),
            TripAction.Pause => trip.Pause(cmd.Sequence, at, now),
            TripAction.Resume => trip.Resume(cmd.Sequence, at, now),
            TripAction.Complete => trip.Complete(cmd.Sequence, at, points, now),
            TripAction.Cancel => trip.Cancel(cmd.Sequence, at, now),
            TripAction.Edit => trip.Edit(cmd.Sequence, cmd.Purpose ?? string.Empty, cmd.Notes, at, now),
            TripAction.Delete => trip.Delete(cmd.Sequence, at, now),
            _ => Result.Failure(TripErrors.Invalid),
        };
    }
}
