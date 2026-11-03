using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters + TenantId explícito: el consumer corre sin tenant del JWT (mismo criterio que el resto).
public sealed class DeliveryNoticeRepository(CorrespondenceDbContext db) : IDeliveryNoticeRepository
{
    public Task<bool> ExistsAsync(
        Guid tenantId,
        Guid sentMessageId,
        string recipient,
        DeliveryOutcome outcome,
        CancellationToken ct = default
    )
    {
        var normalized = (recipient ?? string.Empty).Trim().ToLowerInvariant();
        return db
            .DeliveryNotices.IgnoreQueryFilters()
            .AnyAsync(
                n =>
                    n.TenantId == tenantId
                    && n.SentMessageId == sentMessageId
                    && n.Recipient == normalized
                    && n.Outcome == outcome,
                ct
            );
    }

    public async Task AddAsync(DeliveryNotice notice, CancellationToken ct = default) =>
        await db.DeliveryNotices.AddAsync(notice, ct);

    public Task<DeliveryNotice?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.DeliveryNotices.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id, ct);

    public async Task<IReadOnlyList<DeliveryNotice>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .DeliveryNotices.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.EmailThreadId == emailThreadId)
            .OrderBy(n => n.OccurredAtUtc)
            .ToListAsync(ct);

    // TRACKED: el caller marca leído/no leído y guarda.
    public async Task<IReadOnlyList<DeliveryNotice>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .DeliveryNotices.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.EmailThreadId == emailThreadId)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, DeliveryNotice>> GetLatestByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    )
    {
        if (emailThreadIds.Count == 0)
            return new Dictionary<Guid, DeliveryNotice>();

        var latest = await db
            .DeliveryNotices.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && emailThreadIds.Contains(n.EmailThreadId))
            .GroupBy(n => n.EmailThreadId)
            .Select(g => g.OrderByDescending(n => n.ReceivedAtUtc).First())
            .ToListAsync(ct);
        return latest.ToDictionary(n => n.EmailThreadId);
    }
}
