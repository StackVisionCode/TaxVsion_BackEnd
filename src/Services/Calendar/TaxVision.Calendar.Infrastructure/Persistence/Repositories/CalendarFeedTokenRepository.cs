using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

internal sealed class CalendarFeedTokenRepository(CalendarDbContext context) : ICalendarFeedTokenRepository
{
    public async Task<CalendarFeedToken?> FindActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await context
            .CalendarFeedTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.UserId == userId && t.RevokedAtUtc == null, ct);

    public async Task<CalendarFeedToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default) =>
        await context.CalendarFeedTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public void Add(CalendarFeedToken token) => context.CalendarFeedTokens.Add(token);
}
