using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Rsvp;

public sealed record GetAppointmentRsvpQuery(string Token);

/// <summary>Token desconocido o ya reemplazado = no encontrada: no se distingue uno de otro.</summary>
public static class GetAppointmentRsvpHandler
{
    public static async Task<Result<AppointmentRsvpView>> Handle(
        GetAppointmentRsvpQuery query,
        IAppointmentRsvpLookup lookup,
        ITenantHostResolver hosts,
        CancellationToken ct
    )
    {
        var appointment = await lookup.FindByRsvpTokenAsync(query.Token, ct);
        var attendee = appointment?.FindByRsvpToken(query.Token);
        if (appointment is null || attendee is null)
            return Result.Failure<AppointmentRsvpView>(AppointmentErrors.NotFound);

        var brandingKey = await hosts.ResolveHostAsync(appointment.TenantId, ct);
        return Result.Success(AppointmentRsvpView.Of(appointment, attendee, brandingKey));
    }
}
