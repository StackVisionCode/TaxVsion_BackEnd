using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Directory.Consumers;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Directorio de emails de clientes: los importados llegan con la ficha de cada bloque, sin pedirlos a Customer
/// por HTTP. Un email vacío nunca borra el conocido ni crea una fila, y un evento más viejo no pisa.
/// </summary>
public sealed class CustomerEmailDirectoryConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_new_imported_client_gets_its_directory_row()
    {
        var repo = new InMemoryDirectory();

        await Chunk(repo, T0, Card(isNew: true, " Client@Example.com ", "Client"));

        var row = Assert.Single(repo.Rows);
        Assert.Equal("client@example.com", row.NormalizedEmail);
        Assert.Equal("Client", row.DisplayName);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task An_updated_client_without_email_keeps_the_known_one()
    {
        var repo = new InMemoryDirectory();
        await Chunk(repo, T0, Card(isNew: true, "client@example.com", "Client"));

        await Chunk(repo, T0.AddMinutes(1), Card(isNew: false, null, "Renamed"));

        var row = Assert.Single(repo.Rows);
        Assert.Equal("client@example.com", row.NormalizedEmail);
        Assert.Equal("Renamed", row.DisplayName);
    }

    [Fact]
    public async Task A_new_client_without_email_gets_no_row()
    {
        var repo = new InMemoryDirectory();

        await Chunk(repo, T0, Card(isNew: true, null, "Client"));

        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task An_older_event_does_not_overwrite_a_newer_one()
    {
        var repo = new InMemoryDirectory();
        await Chunk(repo, T0.AddMinutes(5), Card(isNew: true, "new@example.com", "New"));

        await CustomerEmailDirectoryConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = Tenant,
                OccurredOn = T0,
                CustomerId = Customer,
                DisplayName = "Old",
                PrimaryEmail = "old@example.com",
                Language = "En",
                ModifiedByUserId = Guid.NewGuid(),
            },
            repo,
            new NoOpUnitOfWork(),
            default
        );

        var row = Assert.Single(repo.Rows);
        Assert.Equal("new@example.com", row.NormalizedEmail);
        Assert.Equal("New", row.DisplayName);
    }

    // F-E: el email puede llegar después del alta; la fila nace con el estado real del cliente (E-3).
    [Fact]
    public async Task A_client_created_without_email_gets_its_row_when_the_email_arrives()
    {
        var repo = new InMemoryDirectory();
        await Created(repo, T0, email: null);
        Assert.Empty(repo.Rows);

        await Updated(repo, T0.AddMinutes(1), "late@example.com", status: "Active");

        var row = Assert.Single(repo.Rows);
        Assert.Equal("late@example.com", row.NormalizedEmail);
        Assert.True(row.IsActive);
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task An_email_added_to_an_inactive_client_creates_the_row_inactive(string status)
    {
        var repo = new InMemoryDirectory();

        await Updated(repo, T0, "late@example.com", status);

        Assert.False(Assert.Single(repo.Rows).IsActive);
    }

    [Fact]
    public async Task An_old_update_without_status_keeps_the_client_inactive()
    {
        var repo = new InMemoryDirectory();
        await Updated(repo, T0, "client@example.com", status: "Inactive");

        await Updated(repo, T0.AddMinutes(1), "client@example.com", status: null);

        Assert.False(Assert.Single(repo.Rows).IsActive);
    }

    [Fact]
    public async Task An_inactive_imported_client_is_stored_inactive()
    {
        var repo = new InMemoryDirectory();

        await Chunk(repo, T0, Card(isNew: false, "client@example.com", "Client") with { Status = "Inactive" });

        Assert.False(Assert.Single(repo.Rows).IsActive);
    }

    // La conciliación crea la fila con el estado real: un cliente inactivo no queda activo.
    [Fact]
    public async Task The_reconciliation_creates_an_inactive_client_inactive()
    {
        var repo = new InMemoryDirectory();

        await TaxVision.Notification.Infrastructure.Directory.CustomerDirectoryReconciliationJob.UpsertAsync(
            repo,
            new RemoteCustomerContact(Tenant, Customer, "Client", "client@example.com", IsActive: false),
            default
        );

        Assert.False(Assert.Single(repo.Rows).IsActive);
    }

    private static Task Created(InMemoryDirectory repo, DateTime at, string? email) =>
        CustomerEmailDirectoryConsumer.Handle(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = Tenant,
                OccurredOn = at,
                CustomerId = Customer,
                Kind = "Individual",
                DisplayName = "Client",
                PrimaryEmail = email,
                Language = "En",
                CreatedByUserId = Guid.NewGuid(),
            },
            repo,
            new NoOpUnitOfWork(),
            default
        );

    private static Task Updated(InMemoryDirectory repo, DateTime at, string? email, string? status) =>
        CustomerEmailDirectoryConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = Tenant,
                OccurredOn = at,
                CustomerId = Customer,
                DisplayName = "Client",
                PrimaryEmail = email,
                Language = "En",
                Status = status,
                ModifiedByUserId = Guid.NewGuid(),
            },
            repo,
            new NoOpUnitOfWork(),
            default
        );

    // Ya no se pagina la oficina por HTTP: los emails llegan en el aviso de cada bloque.
    [Fact]
    public void The_finished_import_no_longer_asks_customer_for_emails()
    {
        Assert.DoesNotContain(
            typeof(CustomerEmailDirectoryConsumer).GetMethods(),
            m => m.GetParameters().Any(p => p.ParameterType == typeof(CustomersBulkImportedIntegrationEvent))
        );
    }

    private static Task Chunk(InMemoryDirectory repo, DateTime at, ImportedCustomerCard card) =>
        CustomerEmailDirectoryConsumer.Handle(
            new CustomersImportChunkCommittedIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "chunk",
                OccurredOn = at,
                ImportJobId = Guid.NewGuid(),
                ChunkNumber = 1,
                CreatedByUserId = Guid.NewGuid(),
                Customers = [card],
            },
            repo,
            new NoOpUnitOfWork(),
            default
        );

    private static ImportedCustomerCard Card(bool isNew, string? email, string name) =>
        new()
        {
            CustomerId = Customer,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = name,
            PrimaryEmail = email,
            Language = "En",
            Status = "Active",
        };

    private sealed class InMemoryDirectory : ICustomerEmailDirectoryRepository
    {
        public List<CustomerEmailDirectoryEntry> Rows { get; } = [];

        public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default)
        {
            Rows.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
