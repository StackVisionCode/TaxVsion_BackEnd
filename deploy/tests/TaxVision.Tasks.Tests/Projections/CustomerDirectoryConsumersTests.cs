using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Projections.CustomerEvents;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Projections;

public sealed class CustomerDirectoryConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CustomerCreated_inserts_the_entry_and_requests_the_backfill()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository();
        var backfill = new RecordingBackfillService { Needed = true };
        var bus = new FakeMessageBus();
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
                CreatedByUserId = Guid.NewGuid(),
                OccurredOn = T0,
            },
            repository,
            backfill,
            bus,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal("Acme", stored!.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Active, stored.Status);
        Assert.Equal(1, uow.SaveCount);
        Assert.Equal(TenantId, Assert.IsType<BackfillTenantCustomersCommand>(Assert.Single(bus.Published)).TenantId);
        Assert.Empty(backfill.Calls);
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
                CreatedByUserId = Guid.NewGuid(),
                OccurredOn = T0,
            },
            repository,
            new RecordingBackfillService(),
            new FakeMessageBus(),
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
            new FakeMessageBus(),
            new RecordingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal(CustomerDirectoryStatus.Inactive, stored!.Status);
        Assert.Equal("Acme", stored.DisplayName);
    }

    // F-E: el email llega en un Updated más viejo que una baja ya aplicada; entra igual y la baja se conserva.
    [Fact]
    public async Task CustomerUpdated_adds_the_email_after_a_newer_deactivation()
    {
        var customerId = Guid.NewGuid();
        var entry = CustomerDirectoryEntry.Create(TenantId, customerId, "Acme", CustomerDirectoryStatus.Active, T0);
        entry.ApplyStatusIfNewer(CustomerDirectoryStatus.Inactive, T0.AddMinutes(10));
        var repository = new InMemoryCustomerDirectoryRepository(entry);

        await HandleUpdatedAsync(repository, customerId, "late@example.com", status: null, T0.AddMinutes(5));

        Assert.Equal("late@example.com", entry.PrimaryEmailNormalized);
        Assert.Equal(CustomerDirectoryStatus.Inactive, entry.Status);
    }

    // E-3: la fila que nace por un Updated toma el estado que trae el aviso.
    [Fact]
    public async Task CustomerUpdated_back_creates_an_inactive_client_as_inactive()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository();

        await HandleUpdatedAsync(repository, customerId, "late@example.com", status: "Inactive", T0);

        var stored = await repository.GetByCustomerIdAsync(TenantId, customerId);
        Assert.Equal(CustomerDirectoryStatus.Inactive, stored!.Status);
        Assert.Equal("late@example.com", stored.PrimaryEmailNormalized);
    }

    private static Task HandleUpdatedAsync(
        InMemoryCustomerDirectoryRepository repository,
        Guid customerId,
        string? email,
        string? status,
        DateTime at
    ) =>
        CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = TenantId,
                CustomerId = customerId,
                DisplayName = "Acme",
                PrimaryEmail = email,
                Language = "En",
                Status = status,
                ModifiedByUserId = Guid.NewGuid(),
                OccurredOn = at,
            },
            repository,
            new RecordingBackfillService(),
            new FakeMessageBus(),
            new RecordingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

    // TSK-8: un cambio de estado que llega antes que Created se reintenta, no se descarta.
    [Fact]
    public async Task CustomerDeactivated_ArrivesBeforeCreated_ThrowsDependencyNotReady()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var uow = new RecordingUnitOfWork();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            CustomerDeactivatedConsumer.Handle(
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
                new FakeMessageBus(),
                uow,
                new NoOpCorrelationContext(),
                NullLogger<CustomerDirectoryEntry>.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(repository.Entries);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task CustomerReactivated_ArrivesBeforeCreated_ThrowsDependencyNotReady()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var uow = new RecordingUnitOfWork();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            CustomerReactivatedConsumer.Handle(
                new CustomerReactivatedIntegrationEvent
                {
                    TenantId = TenantId,
                    CustomerId = Guid.NewGuid(),
                    ReactivatedByUserId = Guid.NewGuid(),
                    ReactivatedAtUtc = T0,
                    OccurredOn = T0,
                },
                repository,
                new RecordingBackfillService(),
                new FakeMessageBus(),
                uow,
                new NoOpCorrelationContext(),
                NullLogger<CustomerDirectoryEntry>.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(repository.Entries);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task CustomerActivated_ArrivesBeforeCreated_ThrowsDependencyNotReady()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var uow = new RecordingUnitOfWork();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            CustomerActivatedConsumer.Handle(
                new CustomerActivatedIntegrationEvent
                {
                    TenantId = TenantId,
                    CustomerId = Guid.NewGuid(),
                    ActivatedByUserId = Guid.NewGuid(),
                    ActivatedAtUtc = T0,
                    OccurredOn = T0,
                },
                repository,
                new RecordingBackfillService(),
                new FakeMessageBus(),
                uow,
                new NoOpCorrelationContext(),
                NullLogger<CustomerDirectoryEntry>.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(repository.Entries);
        Assert.Equal(0, uow.SaveCount);
    }

    // Cada bloque del import trae la ficha de sus clientes: nombre y email desde el primer momento.
    [Fact]
    public async Task ImportChunk_inserts_new_clients_with_name_and_email()
    {
        var customerId = Guid.NewGuid();
        var repository = new InMemoryCustomerDirectoryRepository();
        var backfill = new RecordingBackfillService { Needed = true };
        var bus = new FakeMessageBus();

        await HandleChunkAsync(
            Chunk(T0, Card(customerId, "Ada Lovelace", " Ada@Example.com ")),
            repository,
            backfill,
            bus
        );

        var entry = Assert.Single(repository.Entries);
        Assert.Equal(customerId, entry.CustomerId);
        Assert.Equal("Ada Lovelace", entry.DisplayName);
        Assert.Equal("ada@example.com", entry.PrimaryEmailNormalized);
        Assert.Equal(CustomerDirectoryStatus.Active, entry.Status);
        Assert.Equal(T0, entry.UpdatedAtUtc);
        Assert.Single(bus.Published.OfType<BackfillTenantCustomersCommand>());
    }

    [Fact]
    public async Task ImportChunk_updates_an_existing_client_with_the_new_card()
    {
        var customerId = Guid.NewGuid();
        var existing = CustomerDirectoryEntry.Create(
            TenantId,
            customerId,
            "Old",
            CustomerDirectoryStatus.Active,
            T0,
            "old@example.com"
        );
        var repository = new InMemoryCustomerDirectoryRepository(existing);

        await HandleChunkAsync(
            Chunk(T0.AddMinutes(5), Card(customerId, "New", "new@example.com", isNew: false)),
            repository
        );

        Assert.Equal("New", existing.DisplayName);
        Assert.Equal("new@example.com", existing.PrimaryEmailNormalized);
        Assert.Equal(T0.AddMinutes(5), existing.UpdatedAtUtc);
    }

    // Un cambio posterior al bloque (p. ej. archivado) no se pisa con la ficha más vieja.
    [Fact]
    public async Task ImportChunk_never_overwrites_a_newer_change()
    {
        var customerId = Guid.NewGuid();
        var existing = CustomerDirectoryEntry.Create(
            TenantId,
            customerId,
            "Archived later",
            CustomerDirectoryStatus.Archived,
            T0.AddMinutes(10)
        );
        var repository = new InMemoryCustomerDirectoryRepository(existing);

        await HandleChunkAsync(Chunk(T0, Card(customerId, "From import", "a@example.com", isNew: false)), repository);

        Assert.Equal("Archived later", existing.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Archived, existing.Status);
        Assert.Equal(T0.AddMinutes(10), existing.UpdatedAtUtc);
    }

    [Fact]
    public async Task ImportChunk_without_email_keeps_the_known_email()
    {
        var customerId = Guid.NewGuid();
        var existing = CustomerDirectoryEntry.Create(
            TenantId,
            customerId,
            "Ada",
            CustomerDirectoryStatus.Active,
            T0,
            "ada@example.com"
        );
        var repository = new InMemoryCustomerDirectoryRepository(existing);

        await HandleChunkAsync(Chunk(T0.AddMinutes(1), Card(customerId, "Ada", null, isNew: false)), repository);

        Assert.Equal("ada@example.com", existing.PrimaryEmailNormalized);
    }

    [Fact]
    public async Task ImportChunk_sends_the_whole_chunk_in_one_upsert()
    {
        var repository = new InMemoryCustomerDirectoryRepository();
        var cards = Enumerable
            .Range(0, 500)
            .Select(i => Card(Guid.NewGuid(), $"Client {i}", $"c{i}@example.com"))
            .ToArray();

        await HandleChunkAsync(Chunk(T0, cards), repository);

        Assert.Equal(500, Assert.Single(repository.ImportedUpserts).Count);
        Assert.Equal(500, repository.Entries.Count);
    }

    // El lote terminado ya no toca el directorio: sus filas por defecto taparían la ficha real del bloque.
    [Fact]
    public void BulkImported_no_longer_feeds_the_directory()
    {
        var handlers = typeof(CustomerCreatedConsumer)
            .Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method =>
                method.Name == "Handle"
                && method.GetParameters().Any(p => p.ParameterType == typeof(CustomersBulkImportedIntegrationEvent))
            );

        Assert.Empty(handlers);
    }

    private static Task HandleChunkAsync(
        CustomersImportChunkCommittedIntegrationEvent evt,
        InMemoryCustomerDirectoryRepository repository,
        RecordingBackfillService? backfill = null,
        FakeMessageBus? bus = null
    ) =>
        CustomersImportChunkCommittedConsumer.Handle(
            evt,
            repository,
            backfill ?? new RecordingBackfillService(),
            bus ?? new FakeMessageBus(),
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

    private static CustomersImportChunkCommittedIntegrationEvent Chunk(
        DateTime occurredOn,
        params ImportedCustomerCard[] cards
    ) =>
        new()
        {
            TenantId = TenantId,
            ImportJobId = Guid.NewGuid(),
            ChunkNumber = 1,
            CreatedByUserId = Guid.NewGuid(),
            Customers = cards,
            OccurredOn = occurredOn,
        };

    private static ImportedCustomerCard Card(Guid customerId, string name, string? email, bool isNew = true) =>
        new()
        {
            CustomerId = customerId,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = name,
            PrimaryEmail = email,
            Language = "En",
            Status = "Active",
        };
}
