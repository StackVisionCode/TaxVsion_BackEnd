using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TripPointsPage(IReadOnlyList<TripPointResponse> Items, long NextAfter, bool HasMore);
