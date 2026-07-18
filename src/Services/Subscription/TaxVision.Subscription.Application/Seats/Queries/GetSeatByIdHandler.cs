using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class GetSeatByIdHandler
{
    public static async Task<Result<SeatResponse>> Handle(
        GetSeatByIdQuery query,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(query.SeatId, query.TenantId, ct);
        return seat is null
            ? Result.Failure<SeatResponse>(new Error("Seat.NotFound", "Seat does not exist."))
            : Result.Success(SeatMapper.ToResponse(seat));
    }
}
