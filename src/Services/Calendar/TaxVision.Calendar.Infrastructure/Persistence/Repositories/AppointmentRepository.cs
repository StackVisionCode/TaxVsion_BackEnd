using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Todas las lecturas van con <c>IgnoreQueryFilters()</c> y el <c>tenantId</c> explicito. El filtro
/// global es fail-closed y en el scope de un job o de un consumer de Wolverine no hay tenant en
/// contexto: devolveria <b>0 filas siempre</b>, y el job pareceria sano.
/// </summary>
public sealed class AppointmentRepository(CalendarDbContext context) : IAppointmentRepository
{
    public async Task<Result<Appointment>> GetByIdAsync(
        Guid tenantId,
        Guid appointmentId,
        CancellationToken ct = default
    )
    {
        var appointment = await Scoped(tenantId)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        return appointment is null
            ? Result.Failure<Appointment>(AppointmentErrors.NotFound)
            : Result.Success(appointment);
    }

    public async Task<IReadOnlyList<Appointment>> ListForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    ) =>
        await Scoped(tenantId)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .Where(a => a.Recurrence != null || (a.Timing.StartUtc < rangeEndUtc && a.Timing.EndUtc > rangeStartUtc))
            .ToListAsync(ct);

    public void Add(Appointment appointment) => context.Appointments.Add(appointment);

    public void Remove(Appointment appointment) => context.Appointments.Remove(appointment);

    private IQueryable<Appointment> Scoped(Guid tenantId) =>
        context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == tenantId);
}
