using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public sealed record DailyMileageQuery(
    Guid TenantId,
    Guid CustomerId,
    string TimeZone,
    int Days = 7,
    DateOnly? EndDate = null
);
