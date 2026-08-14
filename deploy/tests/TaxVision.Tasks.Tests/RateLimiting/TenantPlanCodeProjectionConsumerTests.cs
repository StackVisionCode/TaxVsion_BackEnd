using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.RateLimiting.Abstractions;
using TaxVision.Tasks.Application.RateLimiting.Consumers;
using TaxVision.Tasks.Domain.RateLimiting;

namespace TaxVision.Tasks.Tests.RateLimiting;

/// <summary>
/// El consumer delega en el handler compartido; lo que se prueba acá es que la factory local y el
/// guard de revisión encajan con él, y que la caché se invalida — sin eso el plan viejo sobrevive
/// hasta que expire el TTL y el tenant sigue con la cuota anterior aunque ya cambió de plan.
/// </summary>
public sealed class TenantPlanCodeProjectionConsumerTests
{
    [Fact]
    public async Task Creates_projection_when_none_exists_and_invalidates_cache()
    {
        var tenantId = Guid.NewGuid();
        var repo = new RecordingRepository();
        var cache = new RecordingCacheInvalidator();
        var uow = new NoOpUnitOfWork();

        await TenantPlanCodeProjectionConsumer.Handle(
            NewEvent(tenantId, "enterprise", revisionNumber: 1),
            repo,
            cache,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<TenantPlanCodeProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId);
        Assert.NotNull(stored);
        Assert.Equal("enterprise", stored!.PlanCode);
        Assert.Equal(1, stored.RevisionNumber);
        Assert.Equal(1, uow.SaveCount);
        Assert.Contains(tenantId, cache.Invalidated);
    }

    [Fact]
    public async Task Applies_newer_revision_to_existing_projection()
    {
        var tenantId = Guid.NewGuid();
        var repo = new RecordingRepository(TenantPlanCodeProjection.Create(tenantId, "free", 1));

        await TenantPlanCodeProjectionConsumer.Handle(
            NewEvent(tenantId, "pro", revisionNumber: 2),
            repo,
            new RecordingCacheInvalidator(),
            new NoOpUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<TenantPlanCodeProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId);
        Assert.Equal("pro", stored!.PlanCode);
        Assert.Equal(2, stored.RevisionNumber);
    }

    /// <summary>
    /// Redelivery fuera de orden: un evento con revisión vieja no puede degradar el plan. Sin este
    /// guard un tenant Enterprise vuelve a cuota Free en silencio.
    /// </summary>
    [Fact]
    public async Task Ignores_out_of_order_event_with_older_revision()
    {
        var tenantId = Guid.NewGuid();
        var repo = new RecordingRepository(TenantPlanCodeProjection.Create(tenantId, "enterprise", 5));

        await TenantPlanCodeProjectionConsumer.Handle(
            NewEvent(tenantId, "free", revisionNumber: 4),
            repo,
            new RecordingCacheInvalidator(),
            new NoOpUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<TenantPlanCodeProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId);
        Assert.Equal("enterprise", stored!.PlanCode);
        Assert.Equal(5, stored.RevisionNumber);
    }

    private static TenantEntitlementsChangedIntegrationEvent NewEvent(
        Guid tenantId,
        string planCode,
        long revisionNumber
    ) =>
        new()
        {
            TenantId = tenantId,
            PlanCode = planCode,
            RevisionNumber = revisionNumber,
            ChangedKeys = [],
            SubscriptionStatus = "Active",
            SeatCount = 1,
            AvailableSeatCount = 1,
            EntitlementValues = new Dictionary<string, string>(),
        };

    private sealed class RecordingRepository(params TenantPlanCodeProjection[] seed)
        : ITenantPlanCodeProjectionRepository
    {
        private readonly Dictionary<Guid, TenantPlanCodeProjection> _byTenant = seed.ToDictionary(p => p.TenantId);

        public Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_byTenant.GetValueOrDefault(tenantId));

        public Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default)
        {
            _byTenant[projection.TenantId] = projection;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCacheInvalidator : ITenantPlanCodeCacheInvalidator
    {
        public List<Guid> Invalidated { get; } = [];

        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
        {
            Invalidated.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
