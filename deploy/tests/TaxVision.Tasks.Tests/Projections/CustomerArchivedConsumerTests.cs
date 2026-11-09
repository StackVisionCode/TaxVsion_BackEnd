using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Projections.CustomerEvents;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Tests.Dependencies;

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

    // TSK-7: el HTTP a Customer ya no corre con la transacción del consumidor abierta.
    [Fact]
    public async Task FirstEventOfTenant_EnqueuesBackfill_DoesNotCallCustomerInline()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0)
        );
        var backfill = new RecordingBackfillService { Needed = true };
        var bus = new FakeMessageBus();

        await HandleAsync(repository, new RecordingUnitOfWork(), customerId, backfill, bus);

        Assert.Equal(TenantId, Assert.IsType<BackfillTenantCustomersCommand>(Assert.Single(bus.Published)).TenantId);
        Assert.Empty(backfill.Calls);
        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal(CustomerDirectoryStatus.Archived, stored!.Status);
    }

    [Fact]
    public async Task TenantAlreadyBackfilled_EnqueuesNothing()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0)
        );
        var bus = new FakeMessageBus();

        await HandleAsync(repository, new RecordingUnitOfWork(), customerId, new RecordingBackfillService(), bus);

        Assert.Empty(bus.Published);
    }

    private static Task HandleAsync(
        InMemoryCustomerDirectoryRepository repository,
        RecordingUnitOfWork uow,
        Guid customerId,
        RecordingBackfillService? backfill = null,
        FakeMessageBus? bus = null
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
            backfill ?? new RecordingBackfillService(),
            bus ?? new FakeMessageBus(),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );
}
