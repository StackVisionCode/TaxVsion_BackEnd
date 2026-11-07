using BuildingBlocks.Common;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Backfill;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Tests.Application;

internal sealed class InMemoryDirectory : ICustomerDirectoryRepository
{
    public List<CustomerDirectoryEntry> Entries { get; } = [];

    public Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(Entries.Exists(e => e.TenantId == tenantId && e.CustomerId == customerId));

    public Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(Entries.Find(e => e.TenantId == tenantId && e.CustomerId == customerId)?.DisplayName);

    public Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(Entries.Find(e => e.TenantId == tenantId && e.CustomerId == customerId));

    public Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        DateTime observedAtUtc,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(int limit, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

internal sealed class InMemoryBackfillStates : ITenantBackfillStateRepository
{
    public List<TenantBackfillState> States { get; } = [];

    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(States.Find(s => s.TenantId == tenantId));

    public Task AddAsync(TenantBackfillState entity, CancellationToken ct = default)
    {
        States.Add(entity);
        return Task.CompletedTask;
    }
}

/// <summary>Cuenta las llamadas. <c>null</c> en <see cref="Pages"/> es una página que falla.</summary>
internal sealed class CountingCustomerClient : ICalendarCustomerClient
{
    public List<PagedResult<RemoteCustomerSummary>?> Pages { get; } = [];

    public int ListCalls { get; private set; }

    public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        ListCalls++;
        return Task.FromResult(
            page <= Pages.Count ? Pages[page - 1] : new PagedResult<RemoteCustomerSummary>([], page, size, 0)
        );
    }

    public Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

internal sealed class NoCorrelation : ICorrelationContext
{
    public string CorrelationId => string.Empty;

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}
