using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TrackingInput(long Sequence, DateTimeOffset ObservedAt, IReadOnlyList<GpsPointInput>? Points);
