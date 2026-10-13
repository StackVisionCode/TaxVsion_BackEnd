using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantSubdomainChangedConsumerTests
{
    [Fact]
    public async Task Handle_renames_the_slug_of_an_existing_TenantProfileRef_on_a_primary_change()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(
            TenantProfileRef.Create(tenantId, "Acme Tax Office", "acme-tax", DateTime.UtcNow)
        );
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, isPrimary: true), repository, unitOfWork);

        Assert.Equal("acme-new", existing.SubDomain);
        Assert.Equal("Acme Tax Office", existing.Name);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ignores_a_non_primary_change()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(
            TenantProfileRef.Create(tenantId, "Acme Tax Office", "acme-tax", DateTime.UtcNow)
        );
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(tenantId, isPrimary: false), repository, unitOfWork);

        Assert.Equal("acme-tax", existing.SubDomain);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_skips_when_no_TenantProfileRef_exists()
    {
        var repository = new InMemoryTenantProfileRefRepository();
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Event(Guid.NewGuid(), isPrimary: true), repository, unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static Task HandleAsync(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantSubdomainChangedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantProfileRef>.Instance,
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

    // El fake de Rendering arma una fila nueva en cada lectura; acá hace falta la misma instancia
    // para ver el renombre.
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

        public Task UpsertAsync(
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
