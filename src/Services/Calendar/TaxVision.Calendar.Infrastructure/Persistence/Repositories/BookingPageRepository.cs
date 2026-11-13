using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>Las páginas públicas se leen sin tenant en contexto (visitante anónimo): el tenant va explícito.</summary>
public sealed class BookingPageRepository(CalendarDbContext context) : IBookingPageRepository
{
    public Task<BookingPage?> GetByIdAsync(Guid tenantId, Guid bookingPageId, CancellationToken ct = default) =>
        Pages().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == bookingPageId, ct);

    public async Task<BookingPage?> GetBySlugAsync(Guid tenantId, string slug, CancellationToken ct = default)
    {
        var parsed = BookingPageSlug.Create(slug);
        if (parsed.IsFailure)
            return null;
        var value = parsed.Value;
        return await Pages().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Slug == value, ct);
    }

    public async Task<BookingPage?> FindActiveBySlugAsync(
        IReadOnlyCollection<Guid> tenantIds,
        string slug,
        CancellationToken ct = default
    )
    {
        var parsed = BookingPageSlug.Create(slug);
        if (parsed.IsFailure || tenantIds.Count == 0)
            return null;
        var value = parsed.Value;
        var allowed = tenantIds.ToList();
        return await Pages()
            .FirstOrDefaultAsync(p => allowed.Contains(p.TenantId) && p.IsActive && p.Slug == value, ct);
    }

    public async Task<IReadOnlyList<BookingPage>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await Pages().AsNoTracking().Where(p => p.TenantId == tenantId).OrderBy(p => p.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<BookingPage>> ListByActiveHostAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await Pages()
            .Where(p => p.TenantId == tenantId && p.Hosts.Any(h => h.UserId == userId && h.IsActive))
            .ToListAsync(ct);

    public void Add(BookingPage page) => context.BookingPages.Add(page);

    private IQueryable<BookingPage> Pages() =>
        context
            .BookingPages.IgnoreQueryFilters()
            .Include(p => p.Hosts)
            .Include(p => p.HeardAboutOptions)
            .AsSplitQuery();
}
