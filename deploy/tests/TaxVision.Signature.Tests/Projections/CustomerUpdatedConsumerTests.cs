using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections.CustomerEvents;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Projections;

public sealed class CustomerUpdatedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OlderEventAfterNewer_DoesNotOverwriteEmail()
    {
        var repository = new InMemoryProjections();

        await Handle(repository, "new@example.com", "New Name", Noon);
        await Handle(repository, "old@example.com", "Old Name", Noon.AddMinutes(-5));

        Assert.Equal("new@example.com", repository.Stored!.NormalizedEmail);
        Assert.Equal("New Name", repository.Stored.DisplayName);
    }

    [Fact]
    public async Task NewerEvent_OverwritesEmail()
    {
        var repository = new InMemoryProjections();

        await Handle(repository, "old@example.com", "Old Name", Noon);
        await Handle(repository, "new@example.com", "New Name", Noon.AddMinutes(5));

        Assert.Equal("new@example.com", repository.Stored!.NormalizedEmail);
        Assert.Equal("New Name", repository.Stored.DisplayName);
    }

    // Las filas de antes de la migración no traen fecha: el primer evento siempre aplica.
    [Fact]
    public async Task RowWithoutSourceDate_AppliesTheEvent()
    {
        var repository = new InMemoryProjections
        {
            Stored = CustomerEmailProjection.ForNewCustomer(TenantId, CustomerId, "old@example.com", "Old Name"),
        };

        await Handle(repository, "new@example.com", "New Name", Noon);

        Assert.Equal("new@example.com", repository.Stored!.NormalizedEmail);
    }

    [Fact]
    public async Task LateCreatedAfterUpdated_DoesNotOverwriteEmail()
    {
        var repository = new InMemoryProjections();

        await Handle(repository, "new@example.com", "New Name", Noon);
        await CustomerCreatedConsumer.Handle(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                Kind = "Individual",
                Language = "En",
                CreatedByUserId = Guid.NewGuid(),
                PrimaryEmail = "first@example.com",
                DisplayName = "First Name",
                OccurredOn = Noon.AddHours(-1),
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

        Assert.Equal("new@example.com", repository.Stored!.NormalizedEmail);
    }

    private static Task Handle(InMemoryProjections repository, string email, string name, DateTime occurredOn) =>
        CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                Language = "En",
                ModifiedByUserId = Guid.NewGuid(),
                PrimaryEmail = email,
                DisplayName = name,
                OccurredOn = occurredOn,
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

    private sealed class InMemoryProjections : ICustomerEmailProjectionRepository
    {
        public CustomerEmailProjection? Stored { get; set; }

        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Stored);

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) => Task.FromResult(Stored);

        public Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default)
        {
            Stored = projection;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
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
