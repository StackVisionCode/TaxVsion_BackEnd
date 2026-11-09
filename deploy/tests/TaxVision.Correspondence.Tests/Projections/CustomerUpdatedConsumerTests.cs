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

    private static Task HandleUpdatedAsync(
        Guid tenantId,
        Guid customerId,
        string email,
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
