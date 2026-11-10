using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Rsvp;

public sealed record RespondByRsvpCommand(string Token, AttendeeResponse Response);

/// <summary>La respuesta desde el correo. Avisar al organizador lo hace el evento de respuesta, como en el CRM.</summary>
public static class RespondByRsvpHandler
{
    public static async Task<Result<AppointmentRsvpView>> Handle(
        RespondByRsvpCommand command,
        IAppointmentRsvpLookup lookup,
        ITenantHostResolver hosts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var appointment = await lookup.FindByRsvpTokenAsync(command.Token, ct);
        if (appointment is null)
            return Result.Failure<AppointmentRsvpView>(AppointmentErrors.NotFound);

        var responded = appointment.RespondByRsvp(command.Token, command.Response, DateTime.UtcNow);
        if (responded.IsFailure)
            return Result.Failure<AppointmentRsvpView>(responded.Error);

        await unitOfWork.SaveChangesAsync(ct);
        var brandingKey = await hosts.ResolveHostAsync(appointment.TenantId, ct);
        return Result.Success(
            AppointmentRsvpView.Of(appointment, appointment.FindByRsvpToken(command.Token)!, brandingKey)
        );
    }
}
