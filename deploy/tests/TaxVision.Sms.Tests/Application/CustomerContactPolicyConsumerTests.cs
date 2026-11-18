using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Sms.Application.Customers;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Application.Customers.Consumers;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

/// <summary>
/// La proyección local de la política de contacto (SMS-16): la llenan los eventos de Customer y la
/// reconciliación inicial, sin reactivar un SMS que un evento más nuevo apagó.
/// </summary>
public sealed class CustomerContactPolicyConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private static CustomerCreatedIntegrationEvent Created(bool smsEnabled) =>
        new()
        {
            TenantId = Tenant,
            CustomerId = Customer,
            Kind = "Individual",
            DisplayName = "Client",
            PrimaryEmail = "client@example.com",
            Language = "En",
            SmsEnabled = smsEnabled,
            CreatedByUserId = Guid.NewGuid(),
        };

    private static CustomerUpdatedIntegrationEvent Updated(bool smsEnabled) =>
        new()
        {
            TenantId = Tenant,
            CustomerId = Customer,
            DisplayName = "Client",
            PrimaryEmail = "client@example.com",
            Language = "En",
            SmsEnabled = smsEnabled,
            ModifiedByUserId = Guid.NewGuid(),
        };

    [Fact]
    public async Task Created_event_inserts_the_policy()
    {
        var repo = new FakeCustomerContactPolicyRepository();
        var uow = new FakeUnitOfWork();

        await CustomerContactPolicyConsumer.Handle(Created(smsEnabled: false), repo, uow, CancellationToken.None);

        var row = Assert.Single(repo.Store);
        Assert.False(row.SmsEnabled);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Updated_event_turns_sms_off_on_the_existing_row()
    {
        var repo = new FakeCustomerContactPolicyRepository();
        await CustomerContactPolicyConsumer.Handle(
            Created(smsEnabled: true),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        await CustomerContactPolicyConsumer.Handle(
            Updated(smsEnabled: false),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.False(Assert.Single(repo.Store).SmsEnabled);
    }

    [Fact]
    public async Task A_new_imported_client_gets_its_real_sms_policy()
    {
        var repo = new FakeCustomerContactPolicyRepository();

        await Chunk(repo, DateTime.UtcNow, isNew: true, smsEnabled: false);

        Assert.False(Assert.Single(repo.Store).SmsEnabled);
    }

    [Fact]
    public async Task An_updated_imported_client_changes_its_existing_row()
    {
        var repo = new FakeCustomerContactPolicyRepository();
        var at = DateTime.UtcNow;
        await CustomerContactPolicyConsumer.Handle(
            Created(smsEnabled: true) with
            {
                OccurredOn = at,
            },
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        await Chunk(repo, at.AddMinutes(1), isNew: false, smsEnabled: false);

        Assert.False(Assert.Single(repo.Store).SmsEnabled);
    }

    [Fact]
    public async Task An_older_event_does_not_turn_sms_back_on()
    {
        var repo = new FakeCustomerContactPolicyRepository();
        var at = DateTime.UtcNow;
        await Chunk(repo, at, isNew: true, smsEnabled: false);

        await CustomerContactPolicyConsumer.Handle(
            Updated(smsEnabled: true) with
            {
                OccurredOn = at.AddMinutes(-1),
            },
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.False(Assert.Single(repo.Store).SmsEnabled);
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

    private static Task Chunk(FakeCustomerContactPolicyRepository repo, DateTime at, bool isNew, bool smsEnabled) =>
        CustomerContactPolicyConsumer.Handle(
            new CustomersImportChunkCommittedIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "chunk",
                OccurredOn = at,
                ImportJobId = Guid.NewGuid(),
                ChunkNumber = 1,
                CreatedByUserId = Guid.NewGuid(),
                Customers =
                [
                    new ImportedCustomerCard
                    {
                        CustomerId = Customer,
                        IsNew = isNew,
                        Kind = "Individual",
                        DisplayName = "Client",
                        PrimaryEmail = "client@example.com",
                        Language = "En",
                        Status = "Active",
                        SmsEnabled = smsEnabled,
                    },
                ],
            },
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    [Fact]
    public async Task Snapshot_inserts_a_missing_customer()
    {
        var repo = new FakeCustomerContactPolicyRepository();

        var written = await CustomerContactPolicySnapshot.ApplyAsync(
            repo,
            new RemoteCustomerContactPolicy(Tenant, Customer, SmsEnabled: false),
            DateTime.UtcNow,
            CancellationToken.None
        );

        Assert.True(written);
        Assert.False(Assert.Single(repo.Store).SmsEnabled);
    }

    [Fact]
    public async Task Snapshot_older_than_the_event_does_not_turn_sms_back_on()
    {
        var repo = new FakeCustomerContactPolicyRepository();
        var snapshotAt = DateTime.UtcNow.AddMinutes(-1);
        repo.SmsDisabled(Tenant, Customer);

        var written = await CustomerContactPolicySnapshot.ApplyAsync(
            repo,
            new RemoteCustomerContactPolicy(Tenant, Customer, SmsEnabled: true),
            snapshotAt,
            CancellationToken.None
        );

        Assert.False(written);
        Assert.False(Assert.Single(repo.Store).SmsEnabled);
    }

    [Fact]
    public async Task Snapshot_without_policy_leaves_the_projection_alone()
    {
        // Un Customer viejo que no manda la política no crea ni toca filas.
        var repo = new FakeCustomerContactPolicyRepository();

        var written = await CustomerContactPolicySnapshot.ApplyAsync(
            repo,
            new RemoteCustomerContactPolicy(Tenant, Customer, SmsEnabled: null),
            DateTime.UtcNow,
            CancellationToken.None
        );

        Assert.False(written);
        Assert.Empty(repo.Store);
    }
}
