using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Queries;

/// <summary>Cuándo ocurre la cita antes de responder: separa un RSVP perdido contra otro de uno perdido contra un cambio de hora.</summary>
public sealed record GetAppointmentScheduleStampQuery(Guid TenantId, Guid AppointmentId);

public static class GetAppointmentScheduleStampHandler
{
    public static async Task<Result<string>> Handle(
        GetAppointmentScheduleStampQuery query,
        IAppointmentRepository appointments,
        CancellationToken ct
    )
    {
        var appointment = await appointments.GetByIdAsync(query.TenantId, query.AppointmentId, ct);

        return appointment.IsFailure
            ? Result.Failure<string>(appointment.Error)
            : Result.Success(appointment.Value.ScheduleStamp);
    }
}
