using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Domain.Booking;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class BookingPolicyRepository(CalendarDbContext context) : IBookingPolicyRepository
{
    public Task<BookingPolicy?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        context.BookingPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public void Add(BookingPolicy policy) => context.BookingPolicies.Add(policy);
}
