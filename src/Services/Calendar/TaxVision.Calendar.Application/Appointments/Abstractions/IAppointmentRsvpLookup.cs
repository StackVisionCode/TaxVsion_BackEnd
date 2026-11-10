using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Abstractions;

/// <summary>
/// La cita por el enlace de respuesta de un invitado. Sin sesión no hay tenant: el token lo dice.
/// Aparte del repositorio para no obligar a cada doble de prueba a implementarlo.
/// </summary>
public interface IAppointmentRsvpLookup
{
    Task<Appointment?> FindByRsvpTokenAsync(string token, CancellationToken ct = default);
}
