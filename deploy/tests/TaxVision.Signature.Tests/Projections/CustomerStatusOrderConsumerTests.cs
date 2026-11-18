using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections.CustomerEvents;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Projections;

/// <summary>
/// Estado del cliente en la proyección P-14: un aviso de estado más viejo que el último aplicado no lo revierte,
/// y la fila que nace tarde (el email llegó después del alta) nace con el estado real (E-3).
/// </summary>
public sealed class CustomerStatusOrderConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task An_email_added_to_an_inactive_client_creates_the_row_archived(string status)
    {
        var repository = new InMemoryProjections();

        await Updated(repository, "late@example.com", status, Noon);

        Assert.True(repository.Stored!.IsArchived);
    }

    [Fact]
    public async Task An_update_without_email_creates_nothing()
    {
        var repository = new InMemoryProjections();

        await Updated(repository, null, "Active", Noon);

        Assert.Null(repository.Stored);
    }

    [Fact]
    public async Task A_late_deactivation_does_not_archive_a_reactivated_client()
    {
        var repository = Seeded();
        await Reactivated(repository, Noon.AddMinutes(5));

        await Deactivated(repository, Noon);

        Assert.False(repository.Stored!.IsArchived);
    }

    [Fact]
    public async Task A_late_reactivation_does_not_bring_back_an_archived_client()
    {
        var repository = Seeded();
        await Archived(repository, Noon.AddMinutes(5));

        await Reactivated(repository, Noon);

        Assert.True(repository.Stored!.IsArchived);
    }

    [Fact]
    public async Task A_late_activation_does_not_bring_back_a_deactivated_client()
    {
        var repository = Seeded();
        await Deactivated(repository, Noon.AddMinutes(5));

        await CustomerActivatedConsumer.Handle(
            new CustomerActivatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                ActivatedByUserId = Guid.NewGuid(),
                ActivatedAtUtc = Noon,
                OccurredOn = Noon,
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

        Assert.True(repository.Stored!.IsArchived);
    }

    private static InMemoryProjections Seeded() =>
        new()
        {
            Stored = CustomerEmailProjection.ForNewCustomer(
                TenantId,
                CustomerId,
                "client@example.com",
                "Client",
                Noon.AddHours(-1)
            ),
        };

    private static Task Updated(InMemoryProjections repository, string? email, string status, DateTime at) =>
        CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                Language = "En",
                ModifiedByUserId = Guid.NewGuid(),
                PrimaryEmail = email,
                DisplayName = "Client",
                Status = status,
                OccurredOn = at,
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

    private static Task Deactivated(InMemoryProjections repository, DateTime at) =>
        CustomerDeactivatedConsumer.Handle(
            new CustomerDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                DeactivatedByUserId = Guid.NewGuid(),
                DeactivatedAtUtc = at,
                OccurredOn = at,
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

    private static Task Archived(InMemoryProjections repository, DateTime at) =>
        CustomerArchivedConsumer.Handle(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                ArchivedByUserId = Guid.NewGuid(),
                OccurredOn = at,
            },
            repository,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<CustomerEmailProjection>.Instance,
            CancellationToken.None
        );

    private static Task Reactivated(InMemoryProjections repository, DateTime at) =>
        CustomerReactivatedConsumer.Handle(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = CustomerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = at,
                OccurredOn = at,
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
