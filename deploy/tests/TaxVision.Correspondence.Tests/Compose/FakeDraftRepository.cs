using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Compose;

internal sealed class FakeDraftRepository : IDraftRepository
{
    private readonly List<Draft> _store = [];

    public IReadOnlyList<Draft> All => _store;

    public Task<Draft?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.Id == id));

    public Task<Draft?> FindOpenReplyDraftAsync(
        Guid tenantId,
        Guid customerId,
        Guid incomingEmailId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _store.FirstOrDefault(x =>
                x.TenantId == tenantId
                && x.CustomerId == customerId
                && x.Status == DraftStatus.Draft
                && x.ReplyContext != null
                && x.ReplyContext.IncomingEmailId == incomingEmailId
            )
        );

    public Task AddAsync(Draft entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }

    public Task<PagedResult<Draft>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = size < 1 ? 20 : size;

        var filtered = _store
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId && x.Status == DraftStatus.Draft)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();

        var items = filtered.Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize).ToList();
        return Task.FromResult<PagedResult<Draft>>(
            new PagedResult<Draft>(items, normalizedPage, normalizedSize, filtered.Count)
        );
    }

    public Task<IReadOnlyList<Draft>> ListSentByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<Draft>>(
            _store
                .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId && x.Status == DraftStatus.Sent)
                .ToList()
        );

    public Task<IReadOnlyList<Draft>> ListAbandonedAsync(
        DateTime updatedBeforeUtc,
        int limit,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<Draft>>(
            _store
                .Where(x => x.Status == DraftStatus.Draft && x.UpdatedAtUtc < updatedBeforeUtc)
                .OrderBy(x => x.UpdatedAtUtc)
                .Take(limit)
                .ToList()
        );
}
