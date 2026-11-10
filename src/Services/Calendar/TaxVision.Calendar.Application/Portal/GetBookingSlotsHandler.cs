using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>Los horarios que el cliente puede reservar con ese preparador y tipo, 14 días como máximo por consulta.</summary>
public static class GetBookingSlotsHandler
{
    public static async Task<Result<BookingSlotsResponse>> Handle(
        GetBookingSlotsQuery query,
        IPortalCalendarStore store,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        IBookingPolicyRepository policies,
        CancellationToken ct
    )
    {
        if (
            query.ToUtc <= query.FromUtc
            || query.ToUtc - query.FromUtc > TimeSpan.FromDays(BookingSlotFinder.MaxRangeDays + 1)
        )
            return Result.Failure<BookingSlotsResponse>(BookingErrors.RangeTooLong);

        var bookable = await BookingSlotFinder.CheckAsync(
            query.TenantId,
            query.CustomerId,
            query.TypeId,
            query.PreparerUserId,
            store,
            types,
            availability,
            offices,
            policies,
            ct
        );
        if (bookable.IsFailure)
            return Result.Failure<BookingSlotsResponse>(bookable.Error);

        var slots = await BookingSlotFinder.FindAsync(
            query.TenantId,
            bookable.Value,
            query.FromUtc,
            query.ToUtc,
            query.RescheduleAppointmentId,
            appointments,
            availability,
            types,
            offices,
            ct
        );
        if (slots.IsFailure)
            return Result.Failure<BookingSlotsResponse>(slots.Error);

        var response = new List<Availability.Queries.FreeSlotResponse>();
        foreach (var slot in slots.Value)
            response.Add(new Availability.Queries.FreeSlotResponse(slot.StartUtc, slot.EndUtc));
        return Result.Success(new BookingSlotsResponse(response, bookable.Value.TimeZoneId));
    }
}
