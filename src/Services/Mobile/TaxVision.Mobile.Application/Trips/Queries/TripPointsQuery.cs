using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public sealed record TripPointsQuery(Guid TenantId, Guid CustomerId, Guid TripId, long After = -1, int Size = 100);
