using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
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

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return new PagedResult<EmailThread>(items, normalizedPage, normalizedSize, totalCount);
    }

    private static int ClampPageSize(int requested) =>
        requested switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => requested,
        };
}
