using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Queries;

public sealed record GetAppointmentByIdQuery(Guid TenantId, Guid AppointmentId);

public static class GetAppointmentByIdHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        GetAppointmentByIdQuery query,
        IAppointmentRepository appointments,
        CancellationToken ct
    )
    {
        var appointment = await appointments.GetByIdAsync(query.TenantId, query.AppointmentId, ct);

        return appointment.IsFailure
            ? Result.Failure<AppointmentResponse>(appointment.Error)
            : Result.Success(AppointmentResponse.From(appointment.Value));
    }
}
