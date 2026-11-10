using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas del portal filtradas en SQL por el cliente del token: su <c>CustomerId</c> o un asistente
/// Customer con ese id. Tenant explícito (IgnoreQueryFilters + Where), como el resto de repositorios.
/// </summary>
public sealed class PortalCalendarStore(CalendarDbContext context) : IPortalCalendarStore
{
    public async Task<IReadOnlyList<Appointment>> ListForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default
    ) =>
        await Theirs(tenantId, customerId)
            .Where(a => a.Status != AppointmentStatus.Cancelled)
            .Where(a => a.Recurrence != null || (a.Timing.StartUtc < toUtc && a.Timing.EndUtc > fromUtc))
            .ToListAsync(ct);

    public async Task<Appointment?> FindForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        Guid appointmentId,
        CancellationToken ct = default
    ) => await Theirs(tenantId, customerId).FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

    public async Task<IReadOnlyList<Guid>> ListAssignedStaffAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        await context
            .Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.CustomerId == customerId)
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(ct);

    // Los códigos se guardan como arreglo JSON (["a","b"]): se busca el código entre comillas.
    public async Task<IReadOnlyList<Guid>> ListOfficeStaffAsync(Guid tenantId, CancellationToken ct = default)
    {
        var code = $"\"{CustomersPermissions.ViewAll}\"";
        return await context
            .UserPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive && p.PermissionCodesJson.Contains(code))
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => p.UserId)
            .ToListAsync(ct);
    }

    public async Task<Guid?> FindBookingAsync(
        Guid tenantId,
        Guid customerId,
        string idempotencyKey,
        CancellationToken ct = default
    ) =>
        await context
            .BookingRequests.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.CustomerId == customerId && b.IdempotencyKey == idempotencyKey)
            .Select(b => (Guid?)b.AppointmentId)
            .FirstOrDefaultAsync(ct);

    public void AddBooking(BookingRequest booking) => context.BookingRequests.Add(booking);

    private IQueryable<Appointment> Theirs(Guid tenantId, Guid customerId) =>
        context
            .Appointments.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Where(a => a.CustomerId == customerId || a.Attendees.Any(at => at.CustomerId == customerId))
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions);
}
