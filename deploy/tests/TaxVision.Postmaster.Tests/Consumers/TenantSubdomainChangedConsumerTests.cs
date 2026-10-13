using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Projections.TenantEvents;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// La oficina renombró su subdominio primario: el directorio de Postmaster guarda el slug nuevo. Solo
/// el primario cuenta, y sin fila no hay nada que renombrar (la crea TenantCreated o el backfill).
/// </summary>
public sealed class TenantSubdomainChangedConsumerTests
{
    [Fact]
    public async Task Primary_change_renames_the_slug_and_saves_once()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(
            TenantDirectoryEntry.Create(tenantId, "Acme Tax Office", "acme-tax", DateTime.UtcNow)
        );
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, isPrimary: true), repository, unitOfWork);

        Assert.Equal("acme-new", entry.SubDomain);
        Assert.Equal("Acme Tax Office", entry.Name);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Non_primary_change_leaves_the_entry_untouched()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantDirectoryRepository();
        var entry = repository.Seed(
            TenantDirectoryEntry.Create(tenantId, "Acme Tax Office", "acme-tax", DateTime.UtcNow)
        );
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, isPrimary: false), repository, unitOfWork);

        Assert.Equal("acme-tax", entry.SubDomain);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Unknown_tenant_is_a_noop()
    {
        var repository = new InMemoryTenantDirectoryRepository();
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), isPrimary: true), repository, unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static Task HandleAsync(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantSubdomainChangedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantDirectoryEntry>.Instance,
            CancellationToken.None
        );

    private static TenantSubdomainChangedIntegrationEvent Event(Guid tenantId, bool isPrimary) =>
        new()
        {
            TenantId = tenantId,
            DomainId = Guid.NewGuid(),
            OldHost = "acme-tax.taxproffice.com",
            NewHost = "acme-new.taxproffice.com",
            OldSlug = "acme-tax",
            NewSlug = "acme-new",
            IsPrimary = isPrimary,
        };

    /// <summary>
    /// <see cref="FakeTenantDirectoryRepository"/> arma una fila nueva en cada lectura; para ver el
    /// renombre hace falta devolver siempre la misma instancia.
    /// </summary>
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
