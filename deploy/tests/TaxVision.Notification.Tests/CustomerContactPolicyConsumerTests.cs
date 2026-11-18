using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Directory.Consumers;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Política de contacto de los importados: llega con la ficha de cada bloque (valores reales, no los de por
/// defecto) y un evento más viejo que el ya aplicado no la pisa.
/// </summary>
public sealed class CustomerContactPolicyConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_new_imported_client_gets_its_real_policy_and_language()
    {
        var repo = new InMemoryPolicies();

        await Chunk(repo, T0, Card(isNew: true, email: false, sms: false, "Es"));

        var row = Assert.Single(repo.Rows);
        Assert.False(row.EmailEnabled);
        Assert.False(row.SmsEnabled);
        Assert.Equal("Es", row.Language);
    }

    [Fact]
    public async Task An_updated_imported_client_changes_its_existing_row()
    {
        var repo = new InMemoryPolicies();
        await CustomerContactPolicyConsumer.Handle(Created(T0), repo, new NoOpUnitOfWork(), default);

        await Chunk(repo, T0.AddMinutes(1), Card(isNew: false, email: false, sms: true, "Es"));

        var row = Assert.Single(repo.Rows);
        Assert.False(row.EmailEnabled);
        Assert.Equal("Es", row.Language);
    }

    [Fact]
    public async Task An_older_event_does_not_overwrite_a_newer_one()
    {
        var repo = new InMemoryPolicies();
        await Chunk(repo, T0.AddMinutes(5), Card(isNew: true, email: false, sms: false, "Es"));

        await CustomerContactPolicyConsumer.Handle(Created(T0), repo, new NoOpUnitOfWork(), default);

        var row = Assert.Single(repo.Rows);
        Assert.False(row.EmailEnabled);
        Assert.Equal("Es", row.Language);
    }

    // Sus filas por defecto parecerían más nuevas que la ficha real del bloque y la taparían.
    [Fact]
    public void The_finished_import_no_longer_writes_policy_rows()
    {
        Assert.DoesNotContain(
            typeof(CustomerContactPolicyConsumer).GetMethods(),
            m => m.GetParameters().Any(p => p.ParameterType == typeof(CustomersBulkImportedIntegrationEvent))
        );
    }

    private static Task Chunk(InMemoryPolicies repo, DateTime at, ImportedCustomerCard card) =>
        CustomerContactPolicyConsumer.Handle(
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

    private static ImportedCustomerCard Card(bool isNew, bool email, bool sms, string language) =>
        new()
        {
            CustomerId = Customer,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = "Client",
            PrimaryEmail = "client@example.com",
            Language = language,
            Status = "Active",
            EmailEnabled = email,
            SmsEnabled = sms,
        };

    private static CustomerCreatedIntegrationEvent Created(DateTime at) =>
        new()
        {
            TenantId = Tenant,
            OccurredOn = at,
            CustomerId = Customer,
            Kind = "Individual",
            DisplayName = "Client",
            PrimaryEmail = "client@example.com",
            Language = "En",
            CreatedByUserId = Guid.NewGuid(),
        };

    private sealed class InMemoryPolicies : ICustomerContactPolicyRepository
    {
        public List<CustomerContactPolicyProjection> Rows { get; } = [];

        public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
