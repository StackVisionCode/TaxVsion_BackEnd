using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using TaxVision.FinancialInsights.Infrastructure.Persistence.Repositories;

namespace TaxVision.FinancialInsights.Tests.TestDoubles;

/// <summary>Base en memoria con los repositorios reales y dobles para CloudStorage y Subscription.</summary>
public sealed class FinancialTestHarness : IAsyncDisposable
{
    public FinancialTestHarness(Guid tenantId)
    {
        TenantId = tenantId;
        Tenant = new TestTenantContext(tenantId);
        Db = new FinancialInsightsDbContext(
            new DbContextOptionsBuilder<FinancialInsightsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Tenant
        );
        Batches = new ImportBatchRepository(Db);
        Movements = new FinancialMovementRepository(Db);
        Reservations = new AiReservationRepository(Db);
        Access = new FinancialAccessReader(Db);
        Projections = new FinancialProjectionRepository(Db);
        Entitlements = new TenantEntitlementsResolver(
            Projections,
            Snapshots,
            Cache,
            Db,
            NullLogger<TenantEntitlementsResolver>.Instance
        );
    }

    public Guid TenantId { get; }
    public TestTenantContext Tenant { get; }
    public FinancialInsightsDbContext Db { get; }
    public ImportBatchRepository Batches { get; }
    public FinancialMovementRepository Movements { get; }
    public AiReservationRepository Reservations { get; }
    public FinancialAccessReader Access { get; }
    public FinancialProjectionRepository Projections { get; }
    public TenantEntitlementsResolver Entitlements { get; }
    public FakeImportFileGateway Files { get; } = new();
    public FakeEntitlementsSnapshotClient Snapshots { get; } = new();
    public RecordingCacheInvalidator Cache { get; } = new();
    public FakeCorrelationContext Correlation { get; } = new();

    public IOptions<FinancialCustomerVisibilityOptions> Visibility { get; set; } =
        Options.Create(new FinancialCustomerVisibilityOptions());

    public async Task SeedTenantAsync(bool active = true, params string[] modules)
    {
        var tenant = TenantDirectoryProjection.Create(TenantId, "Office", DateTime.UtcNow.AddMinutes(-5));
        tenant.ApplyStatus(active ? "Active" : "Suspended", active, 1, DateTime.UtcNow.AddMinutes(-4));
        Db.TenantDirectoryProjections.Add(tenant);
        Db.TenantEntitlementsProjections.Add(
            TenantEntitlementsProjection.Create(
                TenantId,
                "enterprise",
                1,
                modules.Length == 0 ? ["financial_insights", "documents"] : modules,
                200
            )
        );
        await Db.SaveChangesAsync();
    }

    public async Task SeedCustomerAsync(Guid customerId, bool active = true, string? name = "Client")
    {
        var row = CustomerDirectoryProjection.Create(TenantId, customerId, name, DateTime.UtcNow.AddMinutes(-5));
        row.Apply(name, active, DateTime.UtcNow.AddMinutes(-4));
        Db.CustomerDirectoryProjections.Add(row);
        await Db.SaveChangesAsync();
    }

    public async Task AssignAsync(Guid customerId, Guid userId)
    {
        Db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(TenantId, customerId, userId, DateTime.UtcNow));
        await Db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public sealed class TestTenantContext(Guid tenant) : ITenantContext
{
    public Guid TenantId { get; private set; } = tenant;
    public bool HasTenant => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}

public sealed class FakeImportFileGateway : IImportFileGateway
{
    private readonly Dictionary<Guid, Queue<ImportFileState>> _states = [];

    public ImportFileState Default { get; set; } = ImportFileState.Ready;
    public int Calls { get; private set; }

    public void Set(Guid fileId, params ImportFileState[] states) => _states[fileId] = new Queue<ImportFileState>(states);

    public Task<ImportFileState> CheckAsync(Guid fileId, Guid customerId, CancellationToken ct = default)
    {
        Calls++;
        if (!_states.TryGetValue(fileId, out var queue) || queue.Count == 0)
            return Task.FromResult(Default);
        return Task.FromResult(queue.Count == 1 ? queue.Peek() : queue.Dequeue());
    }
}

public sealed class FakeEntitlementsSnapshotClient : ITenantEntitlementsSnapshotClient
{
    public TenantEntitlementsSnapshot? Snapshot { get; set; }
    public int Calls { get; private set; }

    public Task<TenantEntitlementsSnapshot?> FetchAsync(Guid tenantId, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Snapshot);
    }
}

public sealed class RecordingCacheInvalidator : ITenantPlanCodeCacheInvalidator
{
    public List<Guid> Invalidated { get; } = [];

    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        Invalidated.Add(tenantId);
        return Task.CompletedTask;
    }
}

public sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test-correlation";

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Restore(() => CorrelationId = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
