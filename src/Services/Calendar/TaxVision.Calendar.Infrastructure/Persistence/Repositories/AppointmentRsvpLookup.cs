using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Busca por el hash, nunca por el valor: en base no hay tokens en claro. Sin sesión no hay tenant,
/// así que ignora el filtro; el token es lo que autoriza.
/// </summary>
public sealed class AppointmentRsvpLookup(CalendarDbContext context) : IAppointmentRsvpLookup
{
    /// <summary>Un token emitido mide 43; algo mucho más largo no se hashea siquiera.</summary>
    private const int MaxTokenLength = 64;

    public async Task<Appointment?> FindByRsvpTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            return null;

        var hash = RsvpToken.HashOf(token);
        return await context
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .FirstOrDefaultAsync(a => a.Attendees.Any(attendee => attendee.RsvpTokenHash == hash), ct);
    }
}
