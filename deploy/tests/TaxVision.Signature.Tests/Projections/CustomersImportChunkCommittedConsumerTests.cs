using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections.CustomerEvents;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Projections;

/// <summary>
/// Cada bloque del import trae la ficha de sus clientes: un firmante importado se reconoce por email sin esperar a
/// la reconciliación. Cada ficha se aplica como el alta (nueva) o la edición (actualizada) de ese cliente.
/// </summary>
public sealed class CustomersImportChunkCommittedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_new_client_is_projected_with_its_email_and_name()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();

        await HandleAsync(repository, Noon, Card(customerId, " New.Client@Example.com ", "New Client"));

        var stored = Assert.Single(repository.All);
        Assert.Equal(TenantId, stored.TenantId);
        Assert.Equal("new.client@example.com", stored.NormalizedEmail);
        Assert.Equal("New Client", stored.DisplayName);
        Assert.False(stored.IsArchived);
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task An_updated_client_gets_its_new_email_and_name()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "old@example.com", "Old", Noon.AddHours(-1)));

        await HandleAsync(repository, Noon, Card(customerId, "new@example.com", "New", isNew: false));

        var stored = Assert.Single(repository.All);
        Assert.Equal("new@example.com", stored.NormalizedEmail);
        Assert.Equal("New", stored.DisplayName);
    }

    [Fact]
    public async Task A_chunk_older_than_the_last_change_is_ignored()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "latest@example.com", "Latest", Noon));

        await HandleAsync(repository, Noon.AddMinutes(-5), Card(customerId, "stale@example.com", "Stale", false));

        Assert.Equal("latest@example.com", Assert.Single(repository.All).NormalizedEmail);
    }

    // El email será opcional: una ficha sin email nunca borra el conocido.
    [Fact]
    public async Task A_card_without_email_never_erases_the_known_one()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "kept@example.com", "Kept", Noon.AddHours(-1)));

        await HandleAsync(repository, Noon, Card(customerId, null, "Renamed", isNew: false));

        var stored = Assert.Single(repository.All);
        Assert.Equal("kept@example.com", stored.NormalizedEmail);
    }

    [Fact]
    public async Task Two_rows_for_the_same_client_keep_the_last_one()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();

        await HandleAsync(
            repository,
            Noon,
            Card(customerId, "first@example.com", "First", isNew: false),
            Card(customerId, "last@example.com", "Last", isNew: false)
        );

        Assert.Equal("last@example.com", Assert.Single(repository.All).NormalizedEmail);
    }

    [Fact]
    public async Task Replaying_the_same_chunk_keeps_one_row_per_client()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();

        await HandleAsync(repository, Noon, Card(customerId, "once@example.com", "Once"));
        await HandleAsync(repository, Noon, Card(customerId, "once@example.com", "Once"));

        Assert.Equal("once@example.com", Assert.Single(repository.All).NormalizedEmail);
    }

    [Fact]
    public async Task A_new_card_reactivates_a_client_archived_before()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();
        var archived = Projection(customerId, "back@example.com", "Back", Noon.AddHours(-1));
        archived.MarkArchived();
        repository.Seed(archived);

        await HandleAsync(repository, Noon, Card(customerId, "back@example.com", "Back"));

        Assert.False(Assert.Single(repository.All).IsArchived);
    }

    // Un cliente que no está activo deja de ser coincidencia de firmante, como con sus eventos de estado.
    [Fact]
    public async Task An_inactive_client_is_no_longer_an_active_match()
    {
        var repository = new InMemoryProjections();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "inactive@example.com", "Inactive", Noon.AddHours(-1)));

        await HandleAsync(repository, Noon, Card(customerId, "inactive@example.com", "Inactive", false, "Inactive"));

        Assert.True(Assert.Single(repository.All).IsArchived);
    }

    [Fact]
    public async Task An_unknown_archived_client_is_not_projected()
    {
        var repository = new InMemoryProjections();

        await HandleAsync(repository, Noon, Card(Guid.NewGuid(), "x@example.com", "X", status: "Archived"));

        Assert.Empty(repository.All);
    }

    private static CustomerEmailProjection Projection(Guid customerId, string email, string name, DateTime at) =>
        CustomerEmailProjection.ForNewCustomer(TenantId, customerId, email, name, at);

    private static ImportedCustomerCard Card(
        Guid customerId,
        string? email,
        string name,
        bool isNew = true,
        string status = "Active"
    ) =>
        new()
        {
            CustomerId = customerId,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = name,
            PrimaryEmail = email,
            Language = "En",
            Status = status,
        };

    private static Task HandleAsync(
        InMemoryProjections repository,
        DateTime occurredOn,
        params ImportedCustomerCard[] cards
    ) =>
        CustomersImportChunkCommittedConsumer.Handle(
            new CustomersImportChunkCommittedIntegrationEvent
            {
                TenantId = TenantId,
                OccurredOn = occurredOn,
                ImportJobId = Guid.NewGuid(),
                ChunkNumber = 1,
                CreatedByUserId = Guid.NewGuid(),
                Customers = cards,
            },
            repository,
            repository,
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

    private sealed class InMemoryProjections : ICustomerEmailProjectionRepository, IUnitOfWork
    {
        private readonly List<CustomerEmailProjection> _rows = [];

        public IReadOnlyList<CustomerEmailProjection> All => _rows;
        public int Saves { get; private set; }

        public void Seed(CustomerEmailProjection projection) => _rows.Add(projection);

        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(_rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _rows.FirstOrDefault(r =>
                    r.TenantId == tenantId && r.NormalizedEmail == normalizedEmail && !r.IsArchived
                )
            );

        public Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default)
        {
            _rows.Add(projection);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
