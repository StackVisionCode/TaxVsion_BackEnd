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

/// <summary>Las citas del cliente en un rango (62 días como máximo), sin las canceladas; las series, por ocurrencia.</summary>
public static class ListPortalAppointmentsHandler
{
    public const int MaxRangeDays = 62;

    public static async Task<Result<IReadOnlyList<PortalAppointmentResponse>>> Handle(
        ListPortalAppointmentsQuery query,
        IPortalCalendarStore store,
        IAppointmentTypeRepository types,
        IBookingPolicyRepository policies,
        IStaffContactClient contacts,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > TimeSpan.FromDays(MaxRangeDays))
            return Result.Failure<IReadOnlyList<PortalAppointmentResponse>>(BookingErrors.RangeTooLong);

        var appointments = await store.ListForCustomerAsync(
            query.TenantId,
            query.CustomerId,
            query.FromUtc,
            query.ToUtc,
            ct
        );
        var mapper = new PortalAppointmentMapper(types, contacts, await policies.GetAsync(query.TenantId, ct));
        var result = new List<PortalAppointmentResponse>();
        foreach (var appointment in appointments)
        {
            if (appointment.Status == AppointmentStatus.Cancelled || !appointment.BelongsToCustomer(query.CustomerId))
                continue;

            var occurrences = OccurrenceExpander.Expand(appointment, query.FromUtc, query.ToUtc);
            if (occurrences.IsFailure)
                continue;
            foreach (var occurrence in occurrences.Value)
                result.Add(await mapper.MapAsync(appointment, occurrence, query.CustomerId, ct));
        }

        result.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return Result.Success<IReadOnlyList<PortalAppointmentResponse>>(result);
    }
}
