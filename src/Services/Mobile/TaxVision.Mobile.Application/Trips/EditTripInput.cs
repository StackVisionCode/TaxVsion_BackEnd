using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record EditTripInput(long Sequence, DateTimeOffset ObservedAt, string Purpose, string? Notes);
