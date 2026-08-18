using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Projections.CustomerEvents;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Tests.Projections;

public sealed class CustomerDirectoryConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CustomerCreated_inserts_the_entry_and_triggers_backfill_first()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository();
        var backfill = new RecordingBackfillService();
        var uow = new RecordingUnitOfWork();

        await CustomerCreatedConsumer.Handle(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = customerId,
                Kind = "Business",
                DisplayName = "Acme",
                PrimaryEmail = "billing@acme.test",
                Language = "En",
                PreferredChannel = "Email",
                CreatedByUserId = Guid.NewGuid(),
                OccurredOn = T0,
            },
            repository,
            backfill,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal("Acme", stored!.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Active, stored.Status);
        Assert.Equal(1, uow.SaveCount);
        Assert.Equal([TenantId], backfill.Calls);
    }

    /// <summary>
    /// El backfill pagina en paralelo con el evento, así que la fila puede existir ya cuando llega
    /// el <c>Created</c>. Reentregarlo no puede duplicar ni fallar.
    /// </summary>
    [Fact]
    public async Task CustomerCreated_is_idempotent_when_the_entry_already_exists()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0)
        );

        await CustomerCreatedConsumer.Handle(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = customerId,
                Kind = "Business",
                DisplayName = "Acme",
                PrimaryEmail = "billing@acme.test",
                Language = "En",
                PreferredChannel = "Email",
                CreatedByUserId = Guid.NewGuid(),
                OccurredOn = T0,
            },
            repository,
            new RecordingBackfillService(),
            new RecordingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Single(repository.Entries);
    }

    [Fact]
    public async Task CustomerDeactivated_flips_the_status_without_erasing_the_name()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0)
        );

        await CustomerDeactivatedConsumer.Handle(
            new CustomerDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = customerId,
                DeactivatedByUserId = Guid.NewGuid(),
                DeactivatedAtUtc = T0.AddMinutes(1),
                OccurredOn = T0.AddMinutes(1),
            },
            repository,
            new RecordingBackfillService(),
            new RecordingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal(CustomerDirectoryStatus.Inactive, stored!.Status);
        Assert.Equal("Acme", stored.DisplayName);
    }

    /// <summary>
    /// Consistencia eventual: el evento de status puede adelantarse al <c>Created</c>. No hay nada
    /// que actualizar, y crear una fila desde acá inventaría un customer sin nombre — el backfill o
    /// la reconciliación la traen después.
    /// </summary>
    [Fact]
    public async Task CustomerDeactivated_is_a_no_op_when_the_entry_is_unknown()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var uow = new RecordingUnitOfWork();

        await CustomerDeactivatedConsumer.Handle(
            new CustomerDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = Guid.NewGuid(),
                DeactivatedByUserId = Guid.NewGuid(),
                DeactivatedAtUtc = T0,
                OccurredOn = T0,
            },
            repository,
            new RecordingBackfillService(),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Empty(repository.Entries);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task CustomersBulkImported_dedupes_created_and_updated_ids()
    {
        var shared = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository();

        await CustomersBulkImportedConsumer.Handle(
            NewBulkEvent([Guid.NewGuid(), shared], [shared, Guid.NewGuid()]),
            repository,
            new RecordingBackfillService(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Single(repository.BulkUpserts);
        Assert.Equal(3, repository.BulkUpserts[0].CustomerIds.Count);
        Assert.All(repository.Entries, e => Assert.Null(e.DisplayName));
    }

    /// <summary>
    /// Un import de 10.000 filas no puede irse en un solo <c>MERGE</c>: el chunk de 500 acota el
    /// tamaño del <c>VALUES</c> generado.
    /// </summary>
    [Fact]
    public async Task CustomersBulkImported_chunks_large_batches()
    {
        var ids = Enumerable.Range(0, 1201).Select(_ => Guid.NewGuid()).ToList();
        var repository = new InMemoryCustomerDirectoryRepository();

        await CustomersBulkImportedConsumer.Handle(
            NewBulkEvent(ids, []),
            repository,
            new RecordingBackfillService(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Equal(3, repository.BulkUpserts.Count);
        Assert.Equal([500, 500, 201], repository.BulkUpserts.Select(u => u.CustomerIds.Count));
    }

    [Fact]
    public async Task CustomersBulkImported_does_nothing_when_no_ids_arrive()
    {
        var repository = new InMemoryCustomerDirectoryRepository();

        await CustomersBulkImportedConsumer.Handle(
            NewBulkEvent([], []),
            repository,
            new RecordingBackfillService(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Empty(repository.BulkUpserts);
    }

    private static CustomersBulkImportedIntegrationEvent NewBulkEvent(
        IReadOnlyList<Guid> created,
        IReadOnlyList<Guid> updated
    ) =>
        new()
        {
            TenantId = TenantId,
            ImportJobId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            CompletedAtUtc = T0,
            TotalRows = created.Count + updated.Count,
            SuccessCount = created.Count,
            UpdatedCount = updated.Count,
            SkippedCount = 0,
            FailedCount = 0,
            CreatedCustomerIds = created,
            UpdatedCustomerIds = updated,
            OccurredOn = T0,
        };
}
