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
