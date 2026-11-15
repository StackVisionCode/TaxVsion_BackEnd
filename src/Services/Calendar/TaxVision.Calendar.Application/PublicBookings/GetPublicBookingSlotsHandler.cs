using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Los comienzos libres del rango, sin decir con quién: el anfitrión se asigna al pedir.</summary>
public static class GetPublicBookingSlotsHandler
{
    public static async Task<Result<PublicBookingSlotsResponse>> Handle(
        GetPublicBookingSlotsQuery query,
        IBookingPageRepository pages,
        PublicBookingScheduler scheduler,
        IOptions<PublicBookingOptions> options,
        CancellationToken ct
    )
    {
        if (
            query.ToUtc <= query.FromUtc
            || query.ToUtc - query.FromUtc > TimeSpan.FromDays(options.Value.MaxSlotRangeDays)
        )
            return Result.Failure<PublicBookingSlotsResponse>(PublicBookingErrors.RangeInvalid);

        var page = await pages.FindActiveBySlugAsync(options.Value.EffectiveTenantIds, query.Slug, ct);
        if (page is null)
            return Result.Failure<PublicBookingSlotsResponse>(PublicBookingErrors.NotFound);
        var type = await scheduler.TypeOfAsync(page, ct);
        if (type is null)
            return Result.Success(new PublicBookingSlotsResponse([]));

        var starts = await scheduler.ListStartsAsync(page, type, query.FromUtc, query.ToUtc, DateTime.UtcNow, ct);
        var slots = new List<PublicBookingSlot>(starts.Count);
        foreach (var start in starts)
            slots.Add(new PublicBookingSlot(start));
        return Result.Success(new PublicBookingSlotsResponse(slots));
    }
}
