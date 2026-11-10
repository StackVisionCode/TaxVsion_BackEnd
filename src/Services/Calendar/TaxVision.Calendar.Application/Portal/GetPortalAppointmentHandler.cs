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

/// <summary>Una cita del cliente. La de otro, o de otro tenant, es 404: no se distingue de una inexistente.</summary>
public static class GetPortalAppointmentHandler
{
    public static async Task<Result<PortalAppointmentResponse>> Handle(
        GetPortalAppointmentQuery query,
        IPortalCalendarStore store,
        IAppointmentTypeRepository types,
        IBookingPolicyRepository policies,
        IStaffContactClient contacts,
        CancellationToken ct
    )
    {
        var appointment = await store.FindForCustomerAsync(query.TenantId, query.CustomerId, query.AppointmentId, ct);
        if (appointment is null || !appointment.BelongsToCustomer(query.CustomerId))
            return Result.Failure<PortalAppointmentResponse>(AppointmentErrors.NotFound);

        var mapper = new PortalAppointmentMapper(types, contacts, await policies.GetAsync(query.TenantId, ct));
        var first = appointment.IsRecurring
            ? OccurrenceExpander.FirstStart(appointment) is { } start
                ? new Occurrence(
                    appointment.Id,
                    start,
                    start,
                    start + (appointment.Timing.Duration ?? TimeSpan.Zero),
                    false,
                    appointment.Title.Value,
                    appointment.Location?.Value
                )
                : null
            : null;
        return Result.Success(await mapper.MapAsync(appointment, first, query.CustomerId, ct));
    }
}
