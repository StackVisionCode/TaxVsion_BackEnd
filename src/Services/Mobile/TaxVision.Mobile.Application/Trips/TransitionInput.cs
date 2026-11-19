using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TransitionInput(long Sequence, DateTimeOffset ObservedAt);
