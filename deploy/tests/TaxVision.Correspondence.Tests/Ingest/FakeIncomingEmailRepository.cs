using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Ingest;

internal sealed class FakeIncomingEmailRepository : IIncomingEmailRepository
{
    private readonly List<IncomingEmail> _store = [];

    public IReadOnlyList<IncomingEmail> All => _store;

    public Task<IncomingEmail?> FindByInternetMessageIdAsync(
        Guid tenantId,
        string internetMessageId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.InternetMessageId == internetMessageId));

    public Task<IncomingEmail?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.Id == id));

    public Task AddAsync(IncomingEmail entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }

    public Task<PagedResult<IncomingEmail>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = size < 1 ? 20 : size;

        var filtered = _store
            .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId)
            .OrderBy(x => x.ReceivedAtUtc)
            .ToList();

        var items = filtered.Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize).ToList();
        return Task.FromResult<PagedResult<IncomingEmail>>(
            new PagedResult<IncomingEmail>(items, normalizedPage, normalizedSize, filtered.Count)
        );
    }

    public Task<IReadOnlyList<IncomingEmail>> ListAllByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<IncomingEmail>>(
            _store
                .Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId)
                .OrderBy(x => x.ReceivedAtUtc)
                .ToList()
        );

    public Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(
            _store
                .Where(x => x.TenantId == tenantId && !x.IsRead && emailThreadIds.Contains(x.EmailThreadId))
                .GroupBy(x => x.EmailThreadId)
                .ToDictionary(g => g.Key, g => g.Count())
        );

    public Task<IReadOnlyList<IncomingEmail>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<IncomingEmail>>(
            _store.Where(x => x.TenantId == tenantId && x.EmailThreadId == emailThreadId).ToList()
        );

    public Task<PagedResult<IncomingEmail>> ListTrashedByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var filtered = _store
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId && x.IsDeleted)
            .OrderByDescending(x => x.DeletedAtUtc)
            .ToList();
        var items = filtered.Skip((page < 1 ? 0 : (page - 1) * size)).Take(size < 1 ? 20 : size).ToList();
        return Task.FromResult<PagedResult<IncomingEmail>>(
            new PagedResult<IncomingEmail>(items, page < 1 ? 1 : page, size < 1 ? 20 : size, filtered.Count)
        );
    }

    public void Remove(IncomingEmail entity) => _store.Remove(entity);

    public Task<IncomingEmail?> FindByAttachmentCloudStorageFileIdAsync(
        Guid tenantId,
        Guid cloudStorageFileId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _store.FirstOrDefault(x =>
                x.TenantId == tenantId && x.Attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId)
            )
        );
}
