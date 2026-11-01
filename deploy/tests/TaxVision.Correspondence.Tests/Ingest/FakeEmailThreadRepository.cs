using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
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
        IReadOnlyCollection<Guid>? visibleAccountIds = null,
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

    /// <summary>Fake: por defecto "visible" (los tests de gate lo controlan si hace falta).</summary>
    public bool HasVisibleMessageResult { get; set; } = true;

    public Task<bool> HasVisibleMessageAsync(
        Guid tenantId,
        Guid threadId,
        IReadOnlyCollection<Guid> visibleAccountIds,
        CancellationToken ct = default
    ) => Task.FromResult(HasVisibleMessageResult);

    /// <summary>Clientes asignados por usuario (gate por cliente); vacío = nadie restringido ve nada de cliente.</summary>
    public HashSet<(Guid UserId, Guid CustomerId)> Assignments { get; } = [];

    public Task<bool> IsCustomerVisibleAsync(
        Guid tenantId,
        Guid customerId,
        Guid? restrictToUserId,
        CancellationToken ct = default
    ) => Task.FromResult(restrictToUserId is not { } user || Assignments.Contains((user, customerId)));

    public Task<PagedResult<EmailThread>> ListVisibleAsync(InboxThreadFilter filter, CancellationToken ct = default)
    {
        var filtered = _store
            .Where(x => x.TenantId == filter.TenantId)
            .Where(x => filter.Status is null || x.Status == filter.Status)
            .Where(x => filter.CustomerId is null || x.CustomerId == filter.CustomerId)
            .Where(x => filter.RestrictToUserId is not { } user || Assignments.Contains((user, x.CustomerId)))
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ToList();
        var page = filter.Page < 1 ? 1 : filter.Page;
        var size = filter.Size < 1 ? 20 : filter.Size;
        return Task.FromResult(
            new PagedResult<EmailThread>(
                filtered.Skip((page - 1) * size).Take(size).ToList(),
                page,
                size,
                filtered.Count
            )
        );
    }

    public UnreadMailCount UnreadCountResult { get; set; } = new(0, 0);

    public Task<UnreadMailCount> CountUnreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    ) => Task.FromResult(UnreadCountResult);
}
