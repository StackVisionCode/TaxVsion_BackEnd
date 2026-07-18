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
            PreferredChannel = "Email",
            ModifiedByUserId = Guid.NewGuid(),
        };

        await CustomerUpdatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
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
            PreferredChannel = "Email",
            ModifiedByUserId = Guid.NewGuid(),
        };

        await CustomerUpdatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.NotNull(stored);
        Assert.Equal("jane.doe@example.com", stored!.EmailAddress);
    }
}
