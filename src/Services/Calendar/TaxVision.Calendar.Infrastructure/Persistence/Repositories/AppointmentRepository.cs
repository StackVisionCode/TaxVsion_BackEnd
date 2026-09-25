using BuildingBlocks.CustomerVisibility;
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

    // Read del detalle con visibilidad por asignación (NO el path de mutación, que usa GetByIdAsync sin filtrar).
    public async Task<Result<Appointment>> GetByIdForReadAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var appointment = await ApplyVisibility(
                Scoped(tenantId).Include(a => a.Attendees).Include(a => a.Exceptions).Where(a => a.Id == appointmentId),
                tenantId,
                assignedToUserId
            )
            .FirstOrDefaultAsync(ct);

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

    public async Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) =>
        await ApplyVisibility(
                Scoped(tenantId)
                    .Include(a => a.Attendees)
                    .Include(a => a.Exceptions)
                    .Where(a =>
                        a.Recurrence != null || (a.Timing.StartUtc < rangeEndUtc && a.Timing.EndUtc > rangeStartUtc)
                    ),
                tenantId,
                assignedToUserId
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
        Guid tenantId,
        Guid userId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    ) =>
        await Scoped(tenantId)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .Where(a => a.OrganizerUserId == userId || a.Attendees.Any(at => at.UserId == userId))
            .Where(a => a.Recurrence != null || (a.Timing.StartUtc < rangeEndUtc && a.Timing.EndUtc > rangeStartUtc))
            .ToListAsync(ct);

    // Tracked (el consumer de offboard reasigna/cancela y persiste). Seek por (TenantId, OrganizerUserId,
    // Status) — las series (StartUtc NULL) entran todas; las puntuales se filtran por fin futuro.
    public async Task<IReadOnlyList<Appointment>> ListFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await Scoped(tenantId)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .Where(a => a.OrganizerUserId == organizerUserId && a.Status != AppointmentStatus.Cancelled)
            .Where(a => a.Recurrence != null || a.Timing.EndUtc > nowUtc)
            .ToListAsync(ct);

    // Mismo filtro que ListFutureByOrganizerAsync (vigentes = series o puntuales que aún no terminan),
    // solo cuenta — pre-flight de impacto al retirarlo.
    public Task<int> CountFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Scoped(tenantId)
            .Where(a => a.OrganizerUserId == organizerUserId && a.Status != AppointmentStatus.Cancelled)
            .Where(a => a.Recurrence != null || a.Timing.EndUtc > nowUtc)
            .CountAsync(ct);

    public void Add(Appointment appointment) => context.Appointments.Add(appointment);

    public void Remove(Appointment appointment) => context.Appointments.Remove(appointment);

    private IQueryable<Appointment> Scoped(Guid tenantId) =>
        context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == tenantId);

    // Visibilidad por asignación (P2): una cita es visible para el actor si NO tiene cliente (interna, no es dato
    // de cliente), la organiza él, o su cliente está asignado a él en la proyección local. IgnoreQueryFilters +
    // tenant explícito en el subquery (scope de Wolverine sin tenant ambiental). null = sin restricción.
    private IQueryable<Appointment> ApplyVisibility(
        IQueryable<Appointment> query,
        Guid tenantId,
        Guid? assignedToUserId
    )
    {
        if (assignedToUserId is not { } assignee)
            return query;
        return query.Where(a =>
            a.CustomerId == null
            || a.OrganizerUserId == assignee
            || context
                .Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .Any(p => p.TenantId == tenantId && p.UserId == assignee && p.CustomerId == a.CustomerId)
        );
    }
}
