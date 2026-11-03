using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Tests.Delivery;

internal sealed class FakeDeliveryNoticeRepository : IDeliveryNoticeRepository
{
    private readonly List<DeliveryNotice> _store = [];

    public IReadOnlyList<DeliveryNotice> All => _store;

    public Task<bool> ExistsAsync(
        Guid tenantId,
        Guid sentMessageId,
        string recipient,
        DeliveryOutcome outcome,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _store.Any(n =>
                n.TenantId == tenantId
                && n.SentMessageId == sentMessageId
                && n.Recipient == recipient.Trim().ToLowerInvariant()
                && n.Outcome == outcome
            )
        );

    public Task AddAsync(DeliveryNotice notice, CancellationToken ct = default)
    {
        _store.Add(notice);
        return Task.CompletedTask;
    }

    public Task<DeliveryNotice?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(n => n.TenantId == tenantId && n.Id == id));

    public Task<IReadOnlyList<DeliveryNotice>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<DeliveryNotice>>(
            _store
                .Where(n => n.TenantId == tenantId && n.EmailThreadId == emailThreadId)
                .OrderBy(n => n.OccurredAtUtc)
                .ToList()
        );

    public Task<IReadOnlyList<DeliveryNotice>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    ) => ListByThreadAsync(tenantId, emailThreadId, ct);

    public Task<IReadOnlyDictionary<Guid, DeliveryNotice>> GetLatestByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DeliveryNotice>>(
            _store
                .Where(n => n.TenantId == tenantId && emailThreadIds.Contains(n.EmailThreadId))
                .GroupBy(n => n.EmailThreadId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.ReceivedAtUtc).First())
        );
}
