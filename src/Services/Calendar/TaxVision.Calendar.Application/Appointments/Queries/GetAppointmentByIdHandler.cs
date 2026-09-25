using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Queries;

// ActorUserId + CanViewAll: visibilidad por asignación (P2) — NotFound si la cita es de un cliente no asignado
// al actor que no ve todo (no filtrar existencia). Sin cliente o si él la organiza, sigue visible.
public sealed record GetAppointmentByIdQuery(Guid TenantId, Guid AppointmentId, Guid ActorUserId, bool CanViewAll);

public static class GetAppointmentByIdHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        GetAppointmentByIdQuery query,
        IAppointmentRepository appointments,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var appointment = await appointments.GetByIdForReadAsync(query.TenantId, query.AppointmentId, assignedTo, ct);

        return appointment.IsFailure
            ? Result.Failure<AppointmentResponse>(appointment.Error)
            : Result.Success(AppointmentResponse.From(appointment.Value));
    }
}
