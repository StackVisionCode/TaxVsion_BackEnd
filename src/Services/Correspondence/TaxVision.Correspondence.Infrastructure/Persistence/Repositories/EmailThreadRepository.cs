using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
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
        var query = db
            .EmailThreads.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId);

        if (visibleAccountIds is null)
        {
            // Sin gate: se oculta solo el hilo SIN contenido visible (0 entrantes vivos y 0 enviados vivos).
            query = query.Where(x =>
                x.MessageCount > 0
                || db.Drafts.Any(d => d.EmailThreadId == x.Id && d.Status == DraftStatus.Sent && d.DeletedAtUtc == null)
            );
        }
        else
        {
            // Gate de buzón de oficina: el hilo debe tener ≥1 mensaje (entrante vivo o enviado) de un
            // buzón visible. Los hilos solo-oficina desaparecen; un hilo mixto se muestra entero.
            var ids = AsArray(visibleAccountIds);
            query = query.Where(x =>
                db.IncomingEmails.Any(ie =>
                    ie.EmailThreadId == x.Id && ie.DeletedAtUtc == null && ids.Contains(ie.AccountId)
                )
                || db.Drafts.Any(d =>
                    d.EmailThreadId == x.Id
                    && d.Status == DraftStatus.Sent
                    && d.DeletedAtUtc == null
                    && ids.Contains(d.AccountId)
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

    private static Guid[] AsArray(IReadOnlyCollection<Guid> ids) => ids as Guid[] ?? ids.ToArray();

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
