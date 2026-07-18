using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class CustomerCreatedConsumerTests
{
    [Fact]
    public async Task Handle_creates_a_new_row_with_primary_email()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerCreatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Kind = "Individual",
            DisplayName = "Jane Doe",
            PrimaryEmail = "Jane.Doe@Example.com",
            Language = "en",
            PreferredChannel = "Email",
            CreatedByUserId = Guid.NewGuid(),
        };

        await CustomerCreatedConsumer.Handle(
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
        Assert.True(stored.IsPrimary);
        Assert.Equal(CustomerEmailSource.CustomerPrimary, stored.Source);
        Assert.True(stored.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_is_idempotent_for_duplicate_events_of_the_same_customer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerCreatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Kind = "Individual",
            DisplayName = "Jane Doe",
            PrimaryEmail = "jane.doe@example.com",
            Language = "en",
            PreferredChannel = "Email",
            CreatedByUserId = Guid.NewGuid(),
        };

        await CustomerCreatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );
        await CustomerCreatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        Assert.Single(repository.All, x => x.TenantId == tenantId && x.CustomerId == customerId);
    }

    [Fact]
    public async Task Handle_skips_projection_when_email_is_invalid()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerCreatedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Kind = "Individual",
            DisplayName = "No Email",
            PrimaryEmail = "not-an-email",
            Language = "en",
            PreferredChannel = "Email",
            CreatedByUserId = Guid.NewGuid(),
        };

        await CustomerCreatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        Assert.Empty(repository.All);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
