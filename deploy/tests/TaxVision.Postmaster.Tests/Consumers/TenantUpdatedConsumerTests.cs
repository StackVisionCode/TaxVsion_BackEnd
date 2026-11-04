using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Projections.TenantEvents;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>La oficina se renombró: el From usa el nombre nuevo; un evento viejo no lo revierte.</summary>
public sealed class TenantUpdatedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rename_updates_the_entry_and_saves_once()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(TenantDirectoryEntry.Create(tenantId, "Acme Tax", "acme-tax", T0.AddDays(-1)));
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, "Acme Tax Group", T0), repository, unitOfWork);

        Assert.Equal("Acme Tax Group", entry.Name);
        Assert.Equal("acme-tax", entry.SubDomain);
        Assert.Equal(T0, entry.SourceUpdatedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Stale_event_is_ignored()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(TenantDirectoryEntry.Create(tenantId, "Acme Tax", "acme-tax", T0));
        entry.ApplyTenantUpdate("Newest", T0, T0);
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, "Older", T0.AddSeconds(-1)), repository, unitOfWork);

        Assert.Equal("Newest", entry.Name);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Unknown_tenant_is_a_noop()
    {
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), "Acme", T0), new InMemoryTenantDirectoryRepository(), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public void Redelivered_creation_or_backfill_keeps_an_applied_rename()
    {
        var entry = TenantDirectoryEntry.Create(Guid.NewGuid(), "Acme Tax", "acme-tax", T0);
        entry.ApplyTenantUpdate("Acme Tax Group", T0, T0);

        entry.Rename("Acme Tax", "acme-new", T0.AddMinutes(1));

        Assert.Equal("Acme Tax Group", entry.Name);
        Assert.Equal("acme-new", entry.SubDomain);
    }

    private static Task HandleAsync(
        TenantUpdatedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantUpdatedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantDirectoryEntry>.Instance,
            CancellationToken.None
        );

    private static TenantUpdatedIntegrationEvent Event(Guid tenantId, string name, DateTime at) =>
        new()
        {
            TenantId = tenantId,
            UpdatedTenantId = tenantId,
            Name = name,
            DefaultTimeZoneId = "America/Chicago",
            UpdatedAtUtc = at,
        };

    private sealed class InMemoryTenantDirectoryRepository : ITenantDirectoryRepository
    {
        private readonly Dictionary<Guid, TenantDirectoryEntry> _rows = [];

        public TenantDirectoryEntry Seed(TenantDirectoryEntry entry)
        {
            _rows[entry.TenantId] = entry;
            return entry;
        }

        public Task<TenantDirectoryEntry?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_rows.GetValueOrDefault(tenantId));

        public Task UpsertAsync(
            Guid tenantId,
            string name,
            string subDomain,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(_rows.Keys.ToHashSet());
    }
}
