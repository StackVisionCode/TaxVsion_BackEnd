using BuildingBlocks.Common;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Tests.Backfill;

public sealed class TenantCustomerBackfillServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Seeds_every_page_and_marks_the_tenant_as_done()
    {
        var client = new StubCustomerClient([
            Page([new RemoteCustomerSummary(Guid.NewGuid(), "Acme", true)], page: 1, totalCount: 2),
            Page([new RemoteCustomerSummary(Guid.NewGuid(), "Globex", false)], page: 2, totalCount: 2),
        ]);
        var directory = new InMemoryCustomerDirectoryRepository();
        var state = new InMemoryTenantBackfillStateRepository();

        await NewService(client, directory, state).EnsureBackfilledAsync(TenantId);

        Assert.Equal(2, directory.Entries.Count);
        Assert.Contains(directory.Entries, e => e.Status == CustomerDirectoryStatus.Inactive);
        Assert.Contains(TenantId, state.CompletedTenantIds);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task Skips_entirely_when_the_tenant_is_already_backfilled()
    {
        var client = new StubCustomerClient([]);
        var state = new InMemoryTenantBackfillStateRepository(TenantId);

        await NewService(client, new InMemoryCustomerDirectoryRepository(), state).EnsureBackfilledAsync(TenantId);

        Assert.Equal(0, client.CallCount);
    }

    /// <summary>
    /// Si Customer no responde a media paginación, marcar el estado dejaría el directorio truncado
    /// para siempre. Al no marcarlo, el próximo evento del tenant reintenta el backfill completo.
    /// </summary>
    [Fact]
    public async Task Does_not_mark_the_state_when_a_page_fails()
    {
        var client = new StubCustomerClient([
            Page([new RemoteCustomerSummary(Guid.NewGuid(), "Acme", true)], page: 1, totalCount: 2),
            null,
        ]);
        var state = new InMemoryTenantBackfillStateRepository();

        await NewService(client, new InMemoryCustomerDirectoryRepository(), state).EnsureBackfilledAsync(TenantId);

        Assert.Empty(state.CompletedTenantIds);
    }

    private static TenantCustomerBackfillService NewService(
        ITasksCustomerClient client,
        InMemoryCustomerDirectoryRepository directory,
        InMemoryTenantBackfillStateRepository state
    ) => new(state, directory, client, new RecordingUnitOfWork(), NullLogger<TenantCustomerBackfillService>.Instance);

    private static PagedResult<RemoteCustomerSummary> Page(
        IReadOnlyList<RemoteCustomerSummary> items,
        int page,
        int totalCount
    ) => new(items, page, Size: 1, totalCount);

    private sealed class StubCustomerClient(IReadOnlyList<PagedResult<RemoteCustomerSummary>?> pages)
        : ITasksCustomerClient
    {
        public int CallCount { get; private set; }

        public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            CallCount++;
            return Task.FromResult(pages[page - 1]);
        }

        public Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
