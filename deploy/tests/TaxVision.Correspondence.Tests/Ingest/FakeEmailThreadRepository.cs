using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Ingest;

internal sealed class FakeEmailThreadRepository : IEmailThreadRepository
{
    private readonly List<EmailThread> _store = [];

    public IReadOnlyList<EmailThread> All => _store;

    public Task<EmailThread?> FindByProviderThreadIdAsync(
        Guid tenantId,
        string providerThreadId,
        CancellationToken ct = default
    ) => Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.ProviderThreadId == providerThreadId));

    public Task<EmailThread?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.Id == id));

    public Task AddAsync(EmailThread entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EmailThread>> FindRecentByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<EmailThread>>(
            _store
                .Where(x =>
                    x.TenantId == tenantId
                    && x.CustomerId == customerId
                    && x.Status == EmailThreadStatus.Active
                    && x.LastMessageAtUtc >= sinceUtc
                )
                .OrderByDescending(x => x.LastMessageAtUtc)
                .ToList()
        );

    public Task<PagedResult<EmailThread>> ListByCustomerAsync(
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
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId)
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ToList();

        var items = filtered.Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize).ToList();
        return Task.FromResult<PagedResult<EmailThread>>(
            new PagedResult<EmailThread>(items, normalizedPage, normalizedSize, filtered.Count)
        );
    }
}
