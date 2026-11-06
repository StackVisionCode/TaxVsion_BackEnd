using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Projections.CustomerEvents;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Tests.Projections;

// Si Archived llega antes que Created, se reintenta en vez de perderse: cuando llega Created, se aplica.
public sealed class CustomerArchivedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ArrivesBeforeCreated_ThrowsDependencyNotReady()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var uow = new RecordingUnitOfWork();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => HandleAsync(repository, uow, Guid.NewGuid()));

        Assert.Empty(repository.Entries);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task ExistingEntry_IsArchived()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0)
        );
        var uow = new RecordingUnitOfWork();

        await HandleAsync(repository, uow, customerId);

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal(CustomerDirectoryStatus.Archived, stored!.Status);
        Assert.Equal(1, uow.SaveCount);
    }

    private static Task HandleAsync(
        InMemoryCustomerDirectoryRepository repository,
        RecordingUnitOfWork uow,
        Guid customerId
    ) =>
        CustomerArchivedConsumer.Handle(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = customerId,
                ArchivedByUserId = Guid.NewGuid(),
                OccurredOn = T0.AddMinutes(5),
            },
            repository,
            new RecordingBackfillService(),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );
}
