using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Projections;

/// <summary>
/// Cada bloque del import trae la ficha de sus clientes: el correo de un importado se vincula sin esperar a la
/// reconciliación. Cada ficha se aplica como el alta (nuevo) o la edición (actualizado) de ese cliente.
/// </summary>
public sealed class CustomersImportChunkCommittedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_new_client_gets_its_email_projected_and_its_earlier_mail_linked()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var backfill = new FakeTenantCustomerBackfillService();
        var customerId = Guid.NewGuid();

        var links = await HandleAsync(repository, Noon, backfill, Card(customerId, "New.Client@Example.com"));

        var stored = Assert.Single(repository.All);
        Assert.Equal(customerId, stored.CustomerId);
        Assert.Equal("new.client@example.com", stored.EmailAddress);
        Assert.Equal(TenantId, Assert.Single(backfill.Calls));
        var link = Assert.IsType<LinkUnlinkedMailCommand>(Assert.Single(links));
        Assert.Equal(new LinkUnlinkedMailCommand(TenantId, customerId, "new.client@example.com"), link);
    }

    [Fact]
    public async Task An_updated_client_gets_its_new_email()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "old@example.com", Noon.AddHours(-1)));

        var links = await HandleAsync(repository, Noon, null, Card(customerId, "new@example.com", isNew: false));

        Assert.Equal("new@example.com", Assert.Single(repository.All).EmailAddress);
        Assert.Single(links);
    }

    [Fact]
    public async Task A_chunk_older_than_the_last_change_is_ignored()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "latest@example.com", Noon));

        var links = await HandleAsync(
            repository,
            Noon.AddMinutes(-5),
            null,
            Card(customerId, "stale@example.com", isNew: false)
        );

        Assert.Equal("latest@example.com", Assert.Single(repository.All).EmailAddress);
        Assert.Empty(links);
    }

    // El email será opcional: una ficha sin email nunca borra el conocido.
    [Fact]
    public async Task A_card_without_email_never_erases_the_known_one()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "kept@example.com", Noon.AddHours(-1)));

        var links = await HandleAsync(repository, Noon, null, Card(customerId, null, isNew: false));

        Assert.Equal("kept@example.com", Assert.Single(repository.All).EmailAddress);
        Assert.Empty(links);
    }

    [Fact]
    public async Task An_email_already_active_for_another_client_is_skipped()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var owner = Guid.NewGuid();
        repository.Seed(Projection(owner, "shared@example.com", Noon.AddHours(-1)));

        var links = await HandleAsync(repository, Noon, null, Card(Guid.NewGuid(), "shared@example.com"));

        Assert.Equal(owner, Assert.Single(repository.All).CustomerId);
        Assert.Empty(links);
    }

    // El índice único de email activo no ve lo agregado en el mismo bloque antes de guardar.
    [Fact]
    public async Task The_same_email_twice_in_one_chunk_is_projected_once()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var first = Guid.NewGuid();

        await HandleAsync(
            repository,
            Noon,
            null,
            Card(first, "twice@example.com"),
            Card(Guid.NewGuid(), "twice@example.com")
        );

        Assert.Equal(first, Assert.Single(repository.All).CustomerId);
    }

    [Fact]
    public async Task Two_rows_for_the_same_client_keep_the_last_one()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();

        await HandleAsync(
            repository,
            Noon,
            null,
            Card(customerId, "first@example.com", isNew: false),
            Card(customerId, "last@example.com", isNew: false)
        );

        Assert.Equal("last@example.com", Assert.Single(repository.All).EmailAddress);
    }

    [Fact]
    public async Task Replaying_the_same_chunk_keeps_one_row_per_client()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();

        await HandleAsync(repository, Noon, null, Card(customerId, "once@example.com"));
        await HandleAsync(repository, Noon, null, Card(customerId, "once@example.com"));

        var stored = Assert.Single(repository.All);
        Assert.Equal("once@example.com", stored.EmailAddress);
        Assert.True(stored.IsActive);
    }

    // Un cliente que no está activo deja de casar remitentes, como con sus eventos de estado.
    [Fact]
    public async Task An_inactive_client_stops_matching_senders()
    {
        var repository = new FakeCustomerEmailAddressRepository();
        var customerId = Guid.NewGuid();
        repository.Seed(Projection(customerId, "inactive@example.com", Noon.AddHours(-1)));

        var links = await HandleAsync(
            repository,
            Noon,
            null,
            Card(customerId, "inactive@example.com", isNew: false, status: "Inactive")
        );

        Assert.False(Assert.Single(repository.All).IsActive);
        Assert.Empty(links);
    }

    [Fact]
    public async Task An_unknown_inactive_client_is_not_projected()
    {
        var repository = new FakeCustomerEmailAddressRepository();

        await HandleAsync(repository, Noon, null, Card(Guid.NewGuid(), "x@example.com", status: "Archived"));

        Assert.Empty(repository.All);
    }

    private static CustomerEmailAddress Projection(Guid customerId, string email, DateTime occurredOn) =>
        CustomerEmailAddress.Create(TenantId, customerId, EmailAddress.Create(email).Value, occurredOn);

    private static ImportedCustomerCard Card(
        Guid customerId,
        string? email,
        bool isNew = true,
        string status = "Active"
    ) =>
        new()
        {
            CustomerId = customerId,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = "Imported Client",
            PrimaryEmail = email,
            Language = "En",
            Status = status,
        };

    private static async Task<IReadOnlyList<object>> HandleAsync(
        FakeCustomerEmailAddressRepository repository,
        DateTime occurredOn,
        FakeTenantCustomerBackfillService? backfill,
        params ImportedCustomerCard[] cards
    ) =>
        await CustomersImportChunkCommittedConsumer.Handle(
            new CustomersImportChunkCommittedIntegrationEvent
            {
                TenantId = TenantId,
                OccurredOn = occurredOn,
                ImportJobId = Guid.NewGuid(),
                ChunkNumber = 1,
                CreatedByUserId = Guid.NewGuid(),
                Customers = cards,
            },
            repository,
            backfill ?? new FakeTenantCustomerBackfillService(),
            new TaxVision.Correspondence.Tests.Ingest.FakeMessageBus(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<CustomerEmailAddress>.Instance,
            CancellationToken.None
        );
}
