using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record DailyMileageResponse(DateOnly Day, double Miles, int TripsCount);
