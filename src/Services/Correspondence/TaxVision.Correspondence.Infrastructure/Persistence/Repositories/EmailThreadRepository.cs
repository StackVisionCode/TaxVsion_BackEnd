using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class EmailThreadRepository(CorrespondenceDbContext db) : IEmailThreadRepository
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public Task<EmailThread?> FindByProviderThreadIdAsync(
        Guid tenantId,
        string providerThreadId,
        CancellationToken ct = default
    ) =>
        db
            .EmailThreads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ProviderThreadId == providerThreadId, ct);

    public Task<EmailThread?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.EmailThreads.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task AddAsync(EmailThread entity, CancellationToken ct = default)
    {
        await db.EmailThreads.AddAsync(entity, ct);
    }

    public async Task<IReadOnlyList<EmailThread>> FindRecentByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        await db
            .EmailThreads.IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenantId
                && x.CustomerId == customerId
                && x.Status == EmailThreadStatus.Active
                && x.LastMessageAtUtc >= sinceUtc
            )
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ToListAsync(ct);

    public async Task<PagedResult<EmailThread>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        IReadOnlyCollection<Guid>? visibleAccountIds = null,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = ClampPageSize(size);

        // AsNoTracking: listado de solo lectura para el cliente final, mismo criterio que
        // CustomerReadService.SearchAsync. Usa IX_EmailThreads_TenantId_CustomerId_LastMessageAtUtc.
        var query = WithMailboxGate(
            db.EmailThreads.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.CustomerId == customerId),
            visibleAccountIds
        );

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return new PagedResult<EmailThread>(items, normalizedPage, normalizedSize, totalCount);
    }

    public async Task<PagedResult<EmailThread>> ListVisibleAsync(
        InboxThreadFilter filter,
        CancellationToken ct = default
    )
    {
        var normalizedPage = filter.Page < 1 ? 1 : filter.Page;
        var normalizedSize = ClampPageSize(filter.Size);

        var query = Visible(filter.TenantId, filter.VisibleAccountIds, filter.RestrictToUserId);
        if (filter.Status is { } status)
            query = query.Where(x => x.Status == status);
        if (filter.CustomerId is { } customerId)
            query = query.Where(x => x.CustomerId == customerId);
        if (filter.Unlinked)
            query = query.Where(x => x.CustomerId == null);
        if (filter.UnreadOnly)
            query = MailVisibilityQueries.ThreadsWithUnread(db, query, filter.TenantId);
        if (filter.HasAttachmentsOnly)
            query = MailVisibilityQueries.ThreadsWithAttachments(db, query);
        query = MailVisibilityQueries.ThreadsInRange(query, filter.FromUtc, filter.ToUtc);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Subject.Contains(term)
                || db.IncomingEmails.Any(ie =>
                    ie.EmailThreadId == x.Id
                    && ie.DeletedAtUtc == null
                    && (ie.From.Contains(term) || ie.FromDisplayName!.Contains(term) || ie.Snippet.Contains(term))
                )
            );
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);
        return new PagedResult<EmailThread>(items, normalizedPage, normalizedSize, totalCount);
    }

    public async Task<UnreadMailCount> CountUnreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    )
    {
        var threads = Visible(tenantId, visibleAccountIds, restrictToUserId)
            .Where(x => x.Status == EmailThreadStatus.Active);
        var unread = await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ie =>
                ie.TenantId == tenantId
                && !ie.IsRead
                && ie.DeletedAtUtc == null
                && threads.Any(t => t.Id == ie.EmailThreadId)
            )
            .GroupBy(ie => ie.EmailThreadId)
            .Select(g => new { ThreadId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Un aviso de no entrega sin leer cuenta como un mensaje sin leer de su hilo.
        var unreadNotices = await MailVisibilityQueries
            .UnreadNotices(db, tenantId)
            .Where(n => threads.Any(t => t.Id == n.EmailThreadId))
            .GroupBy(n => n.EmailThreadId)
            .Select(g => new { ThreadId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var threadCount = unread
            .Select(x => x.ThreadId)
            .Concat(unreadNotices.Select(x => x.ThreadId))
            .Distinct()
            .Count();
        return new UnreadMailCount(threadCount, unread.Sum(x => x.Count) + unreadNotices.Sum(x => x.Count));
    }

    public async Task<bool> IsCustomerVisibleAsync(
        Guid tenantId,
        Guid customerId,
        Guid? restrictToUserId,
        CancellationToken ct = default
    ) =>
        restrictToUserId is not { } userId
        || await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.CustomerId == customerId && a.UserId == userId, ct);

    private IQueryable<EmailThread> Visible(
        Guid tenantId,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId
    ) => MailVisibilityQueries.VisibleThreads(db, tenantId, visibleAccountIds, restrictToUserId);

    private IQueryable<EmailThread> WithMailboxGate(
        IQueryable<EmailThread> query,
        IReadOnlyCollection<Guid>? visibleAccountIds
    ) => MailVisibilityQueries.WithMailboxGate(db, query, visibleAccountIds);

    public async Task<bool> HasVisibleMessageAsync(
        Guid tenantId,
        Guid threadId,
        IReadOnlyCollection<Guid> visibleAccountIds,
        CancellationToken ct = default
    )
    {
        var ids = AsArray(visibleAccountIds);
        if (ids.Length == 0)
            return false;

        var inboundVisible = await db
            .IncomingEmails.IgnoreQueryFilters()
            .AnyAsync(
                ie =>
                    ie.TenantId == tenantId
                    && ie.EmailThreadId == threadId
                    && ie.DeletedAtUtc == null
                    && ids.Contains(ie.AccountId),
                ct
            );
        if (inboundVisible)
            return true;

        return await db
            .Drafts.IgnoreQueryFilters()
            .AnyAsync(
                d =>
                    d.TenantId == tenantId
                    && d.EmailThreadId == threadId
                    && d.Status == DraftStatus.Sent
                    && d.DeletedAtUtc == null
                    && ids.Contains(d.AccountId),
                ct
            );
    }

    private static Guid[] AsArray(IReadOnlyCollection<Guid> ids) => MailVisibilityQueries.AsArray(ids);

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
