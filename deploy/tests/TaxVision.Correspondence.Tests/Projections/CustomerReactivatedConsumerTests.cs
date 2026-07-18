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
        existing.SoftDelete();
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
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(tenantId, customerId);
        Assert.True(stored!.IsActive);
    }
}
