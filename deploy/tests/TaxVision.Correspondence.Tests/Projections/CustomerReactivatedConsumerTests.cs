using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class CustomerReactivatedConsumerTests
{
    [Fact]
    public async Task Handle_reverts_the_soft_delete()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        var existing = CustomerEmailAddress.Create(
            tenantId,
            customerId,
            EmailAddress.Create("jane.doe@example.com").Value
        );
        existing.SoftDelete(DateTime.UtcNow);
        repository.Seed(existing);
        var unitOfWork = new FakeUnitOfWork();
        var evt = new CustomerReactivatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            ReactivatedByUserId = Guid.NewGuid(),
            ReactivatedAtUtc = DateTime.UtcNow,
        };

        await CustomerReactivatedConsumer.Handle(
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
        Assert.True(stored!.IsActive);
    }

    [Fact]
    public async Task Handle_OlderDeactivatedArrivesAfterReactivated_StaysActive()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        repository.Seed(
            CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("jane.doe@example.com").Value)
        );
        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = t1.AddMinutes(5);

        await CustomerReactivatedConsumer.Handle(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = t2,
                OccurredOn = t2,
            },
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );
        await CustomerDeactivatedConsumer.Handle(
            new CustomerDeactivatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                DeactivatedByUserId = Guid.NewGuid(),
                DeactivatedAtUtc = t1,
                OccurredOn = t1,
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
        Assert.True(stored!.IsActive);
    }

    [Fact]
    public async Task Handle_EmailActiveForOtherCustomer_SkipsWithoutThrowing()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new FakeCustomerEmailAddressRepository();
        repository.Seed(
            CustomerEmailAddress.Create(tenantId, Guid.NewGuid(), EmailAddress.Create("shared@example.com").Value)
        );
        var existing = CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("shared@example.com").Value);
        existing.SoftDelete(DateTime.UtcNow.AddDays(-1));
        repository.Seed(existing);
        var unitOfWork = new FakeUnitOfWork();

        await CustomerReactivatedConsumer.Handle(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = DateTime.UtcNow,
            },
            repository,
            new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        Assert.False(existing.IsActive);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
