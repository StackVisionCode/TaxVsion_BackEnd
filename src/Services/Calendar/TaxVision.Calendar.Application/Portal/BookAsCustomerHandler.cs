using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>
/// La reserva del cliente (B7). Dentro del candado del preparador vuelve a comprobar que el horario se
/// ofrecería ahora mismo: dos clientes con el mismo horario → uno lo obtiene, el otro recibe SlotTaken.
/// La clave de idempotencia hace que un reintento devuelva la misma cita.
/// </summary>
public static class BookAsCustomerHandler
{
    public static async Task<Result<PortalAppointmentResponse>> Handle(
        BookAsCustomerCommand command,
        IPortalCalendarStore store,
        ICustomerDirectoryRepository directory,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        IBookingPolicyRepository policies,
        IStaffContactClient contacts,
        IOrganizerScheduleLock scheduleLock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var key = command.IdempotencyKey?.Trim() ?? string.Empty;
        if (key.Length == 0 || key.Length > BookingRequest.MaxKeyLength)
            return Result.Failure<PortalAppointmentResponse>(BookingErrors.IdempotencyKeyRequired);

        var mapper = new PortalAppointmentMapper(types, contacts, await policies.GetAsync(command.TenantId, ct));
        if (
            await store.FindBookingAsync(command.TenantId, command.CustomerId, key, ct) is { } existingId
            && await store.FindForCustomerAsync(command.TenantId, command.CustomerId, existingId, ct) is { } existing
        )
            return Result.Success(await mapper.MapAsync(existing, null, command.CustomerId, ct));

        var bookable = await BookingSlotFinder.CheckAsync(
            command.TenantId,
            command.CustomerId,
            command.TypeId,
            command.PreparerUserId,
            store,
            types,
            availability,
            offices,
            policies,
            ct
        );
        if (bookable.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(bookable.Error);

        // El preparador: el elegido, o el primero libre a esa hora con "Any available".
        Guid? preparer = null;
        foreach (var candidate in bookable.Value.Preparers)
        {
            await scheduleLock.AcquireAsync(command.TenantId, candidate, ct);
            if (
                await BookingSlotFinder.IsFreeAsync(
                    command.TenantId,
                    candidate,
                    bookable.Value,
                    command.StartUtc,
                    null,
                    appointments,
                    availability,
                    types,
                    offices,
                    ct
                )
            )
            {
                preparer = candidate;
                break;
            }
        }

        // Otra vez dentro del candado: dos envíos simultáneos con la misma clave dan la misma cita.
        if (
            await store.FindBookingAsync(command.TenantId, command.CustomerId, key, ct) is { } racedId
            && await store.FindForCustomerAsync(command.TenantId, command.CustomerId, racedId, ct) is { } raced
        )
            return Result.Success(await mapper.MapAsync(raced, null, command.CustomerId, ct));

        if (preparer is not { } organizer)
            return Result.Failure<PortalAppointmentResponse>(BookingErrors.SlotTaken);

        // El nombre del cliente es el de la oficina (directorio); el token del portal no lo trae.
        var customerName =
            await directory.GetDisplayNameAsync(command.TenantId, command.CustomerId, ct)
            ?? (string.IsNullOrWhiteSpace(command.CustomerName) ? null : command.CustomerName.Trim());
        var built = await BuildAsync(
            command with
            {
                CustomerName = customerName,
                PreparerUserId = organizer,
            },
            bookable.Value,
            types,
            contacts,
            ct
        );
        if (built.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(built.Error);

        var appointment = built.Value.Appointment;
        var rules = await Appointments.Commands.ConflictProbe.CheckAsync(
            appointment,
            built.Value.Type,
            appointments,
            availability,
            ct
        );
        if (rules.IsFailure)
            return Result.Failure<PortalAppointmentResponse>(rules.Error);

        appointments.Add(appointment);
        store.AddBooking(
            BookingRequest.Create(command.TenantId, command.CustomerId, key, appointment.Id, DateTime.UtcNow)
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await mapper.MapAsync(appointment, null, command.CustomerId, ct));
    }

    /// <summary>La cita del tipo con el preparador de organizador (aceptado) y el cliente invitado con sus datos de sesión.</summary>
    private static async Task<Result<(Appointment Appointment, Domain.Types.AppointmentType Type)>> BuildAsync(
        BookAsCustomerCommand command,
        BookingSlotFinder.Bookable bookable,
        IAppointmentTypeRepository types,
        IStaffContactClient contacts,
        CancellationToken ct
    )
    {
        var organizerId = command.PreparerUserId!.Value;
        var preparer = await contacts.FindAsync(command.TenantId, organizerId, ct);
        var attendees = new List<Appointments.Commands.ScheduleAttendeeInput>();
        if (!string.IsNullOrWhiteSpace(command.CustomerEmail))
            attendees.Add(
                new Appointments.Commands.ScheduleAttendeeInput(
                    AttendeeKind.Customer,
                    null,
                    command.CustomerId,
                    string.IsNullOrWhiteSpace(command.CustomerName) ? command.CustomerEmail : command.CustomerName,
                    command.CustomerEmail,
                    true,
                    Accepted: true
                )
            );

        var schedule = new Appointments.Commands.ScheduleAppointmentCommand(
            command.TenantId,
            organizerId,
            bookable.Type.Name,
            // Para el equipo (el portal no muestra la descripción): quién la reservó y cómo.
            $"Booked online by {(string.IsNullOrWhiteSpace(command.CustomerName) ? "the client" : command.CustomerName)}.",
            null,
            bookable.Type.Id,
            bookable.TimeZoneId,
            command.StartUtc,
            command.StartUtc + bookable.Type.DefaultDuration,
            null,
            null,
            null,
            null,
            command.CustomerId,
            null,
            bookable.Type.IsVirtual
        )
        {
            Organizer = preparer is null
                ? null
                : new Appointments.Commands.OrganizerContact(preparer.DisplayName, preparer.Email),
            Attendees = attendees,
            BookedByCustomerName = string.IsNullOrWhiteSpace(command.CustomerName) ? "a client" : command.CustomerName,
            // Con aprobación queda pedida: aparta el horario y la oficina decide.
            AwaitingApproval = bookable.Policy.RequiresApproval,
        };

        return await Appointments.Commands.AppointmentFactory.BuildAsync(schedule, types, ct);
    }
}
