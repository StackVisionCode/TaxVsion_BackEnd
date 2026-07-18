using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class CustomerDeactivatedConsumerTests
{
    [Fact]
    public async Task Handle_soft_deletes_the_existing_row()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var existing = CustomerEmailAddress.Create(
            tenantId,
            customerId,
            EmailAddress.Create("jane.doe@example.com").Value
        );
        repository.Seed(existing);
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerDeactivatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            DeactivatedByUserId = Guid.NewGuid(),
            DeactivatedAtUtc = DateTime.UtcNow,
        };

        await CustomerDeactivatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.False(stored!.IsActive);
        Assert.NotNull(stored.DeletedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_is_a_noop_when_the_row_does_not_exist()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerDeactivatedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            DeactivatedByUserId = Guid.NewGuid(),
            DeactivatedAtUtc = DateTime.UtcNow,
        };

        await CustomerDeactivatedConsumer.Handle(
            evt,
            repository,
            new FakeTenantCustomerBackfillService(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
