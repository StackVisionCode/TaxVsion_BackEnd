using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Appointments.Search;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Texto con LIKE y los comodines del usuario escapados. Tenant explícito y la misma visibilidad
/// por asignación que el calendario. En el portal: solo citas del cliente, texto en título y lugar.
/// </summary>
public sealed class AppointmentSearch(CalendarDbContext context) : IAppointmentSearch
{
    private const string Escape = "\\";

    public async Task<IReadOnlyList<Appointment>> SearchAsync(
        AppointmentSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        var tenantId = criteria.TenantId;
        var now = criteria.NowUtc ?? DateTime.UtcNow;
        var query = context
            .Appointments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Where(a =>
                a.Recurrence != null || (a.Timing.StartUtc < criteria.ToUtc && a.Timing.EndUtc > criteria.FromUtc)
            );

        query = criteria.Status switch
        {
            AppointmentSearchStatus.Cancelled => query.Where(a => a.Status == AppointmentStatus.Cancelled),
            AppointmentSearchStatus.Requests => query.Where(a => a.Status == AppointmentStatus.Requested),
            AppointmentSearchStatus.Upcoming => query.Where(a =>
                a.Status != AppointmentStatus.Cancelled && (a.Recurrence != null || a.Timing.EndUtc > now)
            ),
            AppointmentSearchStatus.Past => query.Where(a =>
                a.Status != AppointmentStatus.Cancelled && (a.Recurrence != null || a.Timing.EndUtc <= now)
            ),
            _ => query.Where(a => a.Status != AppointmentStatus.Cancelled),
        };

        if (criteria.PortalCustomerId is { } customer)
        {
            query = query.Where(a => a.CustomerId == customer || a.Attendees.Any(at => at.CustomerId == customer));
        }
        else if (criteria.AssignedToUserId is { } assignee)
        {
            query = query.Where(a =>
                a.CustomerId == null
                || a.OrganizerUserId == assignee
                || context
                    .Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(p => p.TenantId == tenantId && p.UserId == assignee && p.CustomerId == a.CustomerId)
            );
        }

        if (criteria.TypeId is { } type)
            query = query.Where(a => a.AppointmentTypeId == type);
        if (criteria.PersonUserId is { } person)
            query = query.Where(a => a.OrganizerUserId == person || a.Attendees.Any(at => at.UserId == person));
        if (criteria.CustomerId is { } client)
            query = query.Where(a => a.CustomerId == client || a.Attendees.Any(at => at.CustomerId == client));

        if (criteria.Text is { Length: > 0 } text)
        {
            var pattern = $"%{EscapeLike(text)}%";
            // Título y lugar son value objects con conversión: el cast llega a la columna.
            query = criteria.PortalCustomerId is null
                ? query.Where(a =>
                    EF.Functions.Like((string)(object)a.Title, pattern, Escape)
                    || EF.Functions.Like((string)(object)a.Location!, pattern, Escape)
                    || EF.Functions.Like(a.Description!, pattern, Escape)
                    || a.Attendees.Any(at =>
                        EF.Functions.Like(at.Snapshot.DisplayName, pattern, Escape)
                        || EF.Functions.Like(at.Snapshot.Email!, pattern, Escape)
                    )
                )
                : query.Where(a =>
                    EF.Functions.Like((string)(object)a.Title, pattern, Escape)
                    || EF.Functions.Like((string)(object)a.Location!, pattern, Escape)
                );
        }

        // Las series primero y luego lo más lejano en el futuro: si se llena el tope, se cae lo más viejo.
        return await query
            .OrderBy(a => a.Recurrence == null ? 1 : 0)
            .ThenByDescending(a => a.Timing.StartUtc)
            .Take(criteria.Take)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    private static string EscapeLike(string text) =>
        text.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal)
            .Replace("[", Escape + "[", StringComparison.Ordinal);
}
