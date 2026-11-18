using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Application.Projections.CustomerEvents;
using TaxVision.Notes.Domain.Projections;
using Xunit;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Cada bloque guardado del import trae la ficha de sus clientes: el nombre llega desde el primer momento y un cambio
/// posterior al bloque nunca se pisa.
/// </summary>
public sealed class CustomersImportChunkCommittedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task New_clients_arrive_with_their_name_and_status()
    {
        var customerId = Guid.NewGuid();
        var directory = new InMemoryDirectory();

        var backfill = await HandleAsync(Chunk(T0, Card(customerId, "Ada Lovelace")), directory);

        var entry = Assert.Single(directory.Entries);
        Assert.Equal(customerId, entry.CustomerId);
        Assert.Equal("Ada Lovelace", entry.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Active, entry.Status);
        Assert.Equal(T0, entry.UpdatedAtUtc);
        Assert.NotNull(backfill);
    }

    [Fact]
    public async Task An_existing_client_takes_the_newer_card()
    {
        var customerId = Guid.NewGuid();
        var directory = new InMemoryDirectory();
        directory.Entries.Add(
            CustomerDirectoryEntry.Create(TenantId, customerId, "Old", CustomerDirectoryStatus.Active, T0)
        );

        await HandleAsync(Chunk(T0.AddMinutes(5), Card(customerId, "New", isNew: false)), directory);

        var entry = Assert.Single(directory.Entries);
        Assert.Equal("New", entry.DisplayName);
        Assert.Equal(T0.AddMinutes(5), entry.UpdatedAtUtc);
    }

    [Fact]
    public async Task A_newer_change_is_never_overwritten()
    {
        var customerId = Guid.NewGuid();
        var directory = new InMemoryDirectory();
        directory.Entries.Add(
            CustomerDirectoryEntry.Create(
                TenantId,
                customerId,
                "Archived later",
                CustomerDirectoryStatus.Archived,
                T0.AddMinutes(10)
            )
        );

        await HandleAsync(Chunk(T0, Card(customerId, "From import", isNew: false)), directory);

        var entry = Assert.Single(directory.Entries);
        Assert.Equal("Archived later", entry.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Archived, entry.Status);
    }

    [Fact]
    public async Task The_whole_chunk_goes_in_one_upsert()
    {
        var directory = new InMemoryDirectory();
        var cards = Enumerable.Range(0, 500).Select(i => Card(Guid.NewGuid(), $"Client {i}")).ToArray();

        await HandleAsync(Chunk(T0, cards), directory);

        Assert.Equal(500, Assert.Single(directory.ImportedUpserts).Count);
        Assert.Equal(500, directory.Entries.Count);
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

    private static Task<RunTenantCustomerBackfillCommand?> HandleAsync(
        CustomersImportChunkCommittedIntegrationEvent evt,
        InMemoryDirectory directory
    ) =>
        CustomersImportChunkCommittedConsumer.Handle(
            evt,
            directory,
            new InMemoryBackfillStates(),
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

    private static ImportedCustomerCard Card(Guid customerId, string name, bool isNew = true) =>
        new()
        {
            CustomerId = customerId,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = name,
            PrimaryEmail = "client@example.com",
            Language = "En",
            Status = "Active",
        };
}
