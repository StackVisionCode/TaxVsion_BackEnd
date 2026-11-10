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

/// <summary>
/// El cliente mueve su cita a otro horario ofrecido, con el mismo preparador y la misma duración. Se
/// revalida como una reserva; las respuestas vuelven a cero y los enlaces de respuesta cambian.
/// </summary>
public static class RescheduleAsCustomerHandler
{
    public static async Task<Result<PortalAppointmentResponse>> Handle(
        RescheduleAsCustomerCommand command,
        IPortalCalendarStore store,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        IBookingPolicyRepository policies,
        IOrganizerScheduleLock scheduleLock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var appointment = await store.FindForCustomerAsync(
            command.TenantId,
            command.CustomerId,
            command.AppointmentId,
            ct
        );
        if (appointment is null)
            return Result.Failure<PortalAppointmentResponse>(AppointmentErrors.NotFound);

        var policy = await policies.GetAsync(command.TenantId, ct);
        var check = PortalChange.Allowed(appointment, policy);
        if (check.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(check.Error);

        var type = await types.GetByIdAsync(command.TenantId, appointment.AppointmentTypeId, ct);
        if (type.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(BookingErrors.NotAvailable);

        var duration = (appointment.Timing.EndUtc - appointment.Timing.StartUtc) ?? type.Value.DefaultDuration;
        var zone = await BookingSlotFinder.TimeZoneAsync(
            command.TenantId,
            appointment.OrganizerUserId,
            availability,
            offices,
            ct
        );
        var bookable = new BookingSlotFinder.Bookable(policy!, type.Value, zone, [appointment.OrganizerUserId]);

        await scheduleLock.AcquireAsync(command.TenantId, appointment.OrganizerUserId, ct);
        var free = await BookingSlotFinder.IsFreeAsync(
            command.TenantId,
            appointment.OrganizerUserId,
            bookable,
            command.StartUtc,
            appointment.Id,
            appointments,
            availability,
            types,
            offices,
            ct
        );
        if (!free)
            return Result.Failure<PortalAppointmentResponse>(BookingErrors.SlotTaken);

        var timing = Domain.ValueObjects.EventTiming.PointInTimeOf(
            command.StartUtc,
            command.StartUtc + duration,
            appointment.Timing.TimeZone.Id
        );
        if (timing.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(timing.Error);

        var moved = appointment.RescheduleByCustomer(
            timing.Value,
            command.CustomerId,
            command.PortalUserId,
            DateTime.UtcNow
        );
        if (moved.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(moved.Error);

        await unitOfWork.SaveChangesAsync(ct);
        var mapper = new PortalAppointmentMapper(types, new NoContacts(), policy);
        return Result.Success(await mapper.MapAsync(appointment, null, command.CustomerId, ct));
    }

    /// <summary>El nombre del preparador ya está en la cita: no hace falta preguntar a Auth.</summary>
    private sealed class NoContacts : IStaffContactClient
    {
        public Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<StaffContact?>(null);
    }
}
