using BuildingBlocks.Messaging;
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

    // BB-7, como TenantProfileChanged: el cambio que llega antes que TenantCreated se reintenta, no se descarta.
    [Fact]
    public async Task Change_before_the_row_exists_is_retried_and_applies_once_it_exists()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var unitOfWork = new FakeUnitOfWork();
        var change = Event(tenantId, isPrimary: true);

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => HandleAsync(change, repository, unitOfWork));
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);

        var created = repository.Seed(TenantProfileRef.Create(tenantId, "Acme Tax Office", "acme-tax", DateTime.UtcNow));
        await HandleAsync(change, repository, unitOfWork);

        Assert.Equal("acme-new", created.SubDomain);
    }

    // SCR-04: el mensaje viejo que llega tarde no deshace el cambio más nuevo.
    [Fact]
    public async Task Older_slug_event_does_not_revert_a_newer_one()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryTenantProfileRefRepository();
        var existing = repository.Seed(TenantProfileRef.Create(tenantId, "Acme", "start", DateTime.UtcNow));
        var unitOfWork = new FakeUnitOfWork();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        await HandleAsync(
            Event(tenantId, true) with
            {
                NewSlug = "b",
                OccurredOn = t1.AddMinutes(1),
            },
            repository,
            unitOfWork
        );
        await HandleAsync(Event(tenantId, true) with { NewSlug = "a", OccurredOn = t1 }, repository, unitOfWork);

        Assert.Equal("b", existing.SubDomain);
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
