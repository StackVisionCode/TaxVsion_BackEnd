using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class CustomerActivatedConsumerTests
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
        var evt = new CustomerActivatedIntegrationEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            ActivatedByUserId = Guid.NewGuid(),
            ActivatedAtUtc = DateTime.UtcNow,
        };

        await CustomerActivatedConsumer.Handle(
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
        Assert.Null(stored.DeletedAtUtc);
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

        await CustomerActivatedConsumer.Handle(
            new CustomerActivatedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                ActivatedByUserId = Guid.NewGuid(),
                ActivatedAtUtc = DateTime.UtcNow,
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
