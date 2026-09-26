using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Xunit;

namespace TaxVision.Billing.Tests.Customers;

/// <summary>
/// El consumer COMPARTIDO del kit (BuildingBlocks.CustomerVisibility) aplica el snapshot solo si su Version es
/// más nueva que la última aplicada (event-carried state transfer + idempotencia por versión; descarta
/// reordenados/duplicados). Billing lo consume vía IncludeType sobre el store del kit (misma tabla que la
/// impl inline anterior, ya consolidada).
/// </summary>
public sealed class CustomerAssignmentsProjectionConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid U1 = Guid.NewGuid();
    private static readonly Guid U2 = Guid.NewGuid();

    [Fact]
    public async Task Applies_a_newer_snapshot()
    {
        var repo = new FakeRepo();

        await Consume(repo, [U1, U2], new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, repo.ReplaceCalls);
        Assert.Equal([U1, U2], repo.LastUsers);
    }

    [Fact]
    public async Task Ignores_a_stale_snapshot()
    {
        var repo = new FakeRepo { Version = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc) };

        // Evento más viejo que lo ya aplicado → se ignora (reordenado/duplicado).
        await Consume(repo, [U1], new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, repo.ReplaceCalls);
    }

    private static Task Consume(FakeRepo repo, Guid[] users, DateTime version) =>
        CustomerAssignmentsProjectionConsumer.Handle(
            new CustomerAssignmentsChangedIntegrationEvent
            {
                TenantId = Tenant,
                CustomerId = Customer,
                AssigneeUserIds = users,
                Version = version,
            },
            repo,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            CancellationToken.None
        );

    private sealed class FakeRepo : ICustomerAssignmentProjectionStore
    {
        public DateTime? Version { get; set; }
        public int ReplaceCalls { get; private set; }
        public IReadOnlyCollection<Guid> LastUsers { get; private set; } = [];

        public Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Version);

        public Task ReplaceAsync(
            Guid tenantId,
            Guid customerId,
            IReadOnlyCollection<Guid> userIds,
            DateTime version,
            CancellationToken ct = default
        )
        {
            ReplaceCalls++;
            LastUsers = userIds;
            Version = version;
            return Task.CompletedTask;
        }

        public Task<bool> IsAnyAssignedToUserAsync(
            Guid tenantId,
            Guid userId,
            IReadOnlyCollection<Guid> customerIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
