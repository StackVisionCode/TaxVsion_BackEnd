using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class CustomerUpdatedConsumerTests
{
    [Fact]
    public async Task Handle_upserts_the_email_on_the_same_row_for_the_customer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var existing = CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("old@example.com").Value);
        repository.Seed(existing);
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerUpdatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            DisplayName = "Jane Doe",
            PrimaryEmail = "New.Email@Example.com",
            Language = "en",
            ModifiedByUserId = Guid.NewGuid(),
        };

        await CustomerUpdatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.Same(existing, stored);
        Assert.Equal("new.email@example.com", stored!.EmailAddress);
        Assert.Single(repository.All, x => x.TenantId == tenantId && x.CustomerId == customerId);
    }

    [Fact]
    public async Task Handle_back_creates_the_row_when_the_customer_is_unknown()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerUpdatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            DisplayName = "Jane Doe",
            PrimaryEmail = "jane.doe@example.com",
            Language = "en",
            ModifiedByUserId = Guid.NewGuid(),
        };

        await CustomerUpdatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.NotNull(stored);
        Assert.Equal("jane.doe@example.com", stored!.EmailAddress);
    }

    // F-E: el email llega después del alta; si el cliente ya está inactivo, la fila nace inactiva (E-3) y no casa correo.
    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task Handle_back_creates_an_inactive_client_as_inactive(string status)
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();

        var link = await CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                DisplayName = "Jane Doe",
                PrimaryEmail = "late@example.com",
                Language = "en",
                Status = status,
                ModifiedByUserId = Guid.NewGuid(),
                OccurredOn = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc),
            },
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.NotNull(stored!.DeletedAtUtc);
        Assert.Null(link);
    }

    [Fact]
    public async Task Handle_skips_an_update_without_email()
    {
        var repository = new FakeCustomerEmailAddressRepository();

        var link = await HandleUpdatedAsync(Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, repository);

        Assert.Empty(repository.All);
        Assert.Null(link);
    }

    private static Task<TaxVision.Correspondence.Application.Ingest.LinkUnlinkedMailCommand?> HandleUpdatedAsync(
        Guid tenantId,
        Guid customerId,
        string? email,
        DateTime occurredOn,
        FakeCustomerEmailAddressRepository repository
    ) =>
        CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                DisplayName = "Jane Doe",
                PrimaryEmail = email,
                Language = "en",
                ModifiedByUserId = Guid.NewGuid(),
                OccurredOn = occurredOn,
            },
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_EventsOutOfOrder_ProjectionKeepsNewestEmail()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var t1 = DateTime.UtcNow.AddMinutes(-30);
        repository.Seed(CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("x@example.com").Value));

        await HandleUpdatedAsync(tenantId, customerId, "z@example.com", t1.AddMinutes(20), repository);
        await HandleUpdatedAsync(tenantId, customerId, "y@example.com", t1.AddMinutes(10), repository);

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.Equal("z@example.com", stored!.EmailAddress);
    }
}
