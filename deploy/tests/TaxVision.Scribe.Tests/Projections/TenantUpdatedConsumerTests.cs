using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantUpdatedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_renames_an_existing_profile_and_saves()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0.AddDays(-1)));
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, "Acme Tax Group", T0), repository, unitOfWork);

        Assert.Equal("Acme Tax Group", existing.Name);
        Assert.Equal("acme-tax", existing.SubDomain);
        Assert.Equal(T0, existing.SourceUpdatedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ignores_an_event_older_than_the_applied_one()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax", "acme-tax", T0));
        existing.ApplyTenantUpdate("Newest", T0, T0);
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, "Older", T0.AddSeconds(-1)), repository, unitOfWork);

        Assert.Equal("Newest", existing.Name);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_skips_when_no_profile_exists()
    {
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), "Acme", T0), new InMemoryTenantProfileRefRepository(), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static Task HandleAsync(
        TenantUpdatedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantUpdatedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantProfileRef>.Instance,
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

    private sealed class InMemoryTenantProfileRefRepository : ITenantProfileRefRepository
    {
        private readonly Dictionary<Guid, TenantProfileRef> _rows = [];

        public TenantProfileRef Seed(TenantProfileRef row)
        {
            _rows[row.TenantId] = row;
            return row;
        }

        public Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_rows.GetValueOrDefault(tenantId));

        public Task AddIfMissingAsync(
            Guid tenantId,
            string name,
            string subDomain,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(_rows.Keys.ToList());
    }
}
