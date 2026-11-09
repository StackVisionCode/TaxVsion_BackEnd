using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Política de contacto contra el SQL Server real (user-secrets del Api o CUSTOMER_DB_CONNECTION).
/// Cada cambio se guarda y se relee en contextos distintos, como en un PATCH. Tenant propio y limpieza.
/// </summary>
public sealed class CustomerContactPolicyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly string _connectionString = CustomerIntegrationDatabase.ConnectionString;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CustomerDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenant)
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Sms_turned_off_and_back_on_persists_both_changes()
    {
        var id = await SeedAsync(ContactPolicy.Default);

        await ChangeAsync(id, emailEnabled: true, smsEnabled: false);
        Assert.Equal((true, false), await ReadAsync(id));

        await ChangeAsync(id, emailEnabled: true, smsEnabled: true);
        Assert.Equal((true, true), await ReadAsync(id));
    }

    [Fact]
    public async Task Email_turned_off_and_back_on_persists_both_changes()
    {
        var id = await SeedAsync(ContactPolicy.Default);

        await ChangeAsync(id, emailEnabled: false, smsEnabled: true);
        Assert.Equal((false, true), await ReadAsync(id));

        await ChangeAsync(id, emailEnabled: true, smsEnabled: true);
        Assert.Equal((true, true), await ReadAsync(id));
    }

    [Fact]
    public async Task Customer_registered_with_both_channels_off_persists_them_off()
    {
        var id = await SeedAsync(new ContactPolicy(EmailEnabled: false, SmsEnabled: false));

        Assert.Equal((false, false), await ReadAsync(id));
    }

    private async Task<Guid> SeedAsync(ContactPolicy policy)
    {
        var customer = DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Policy", "Client").Value,
                null,
                EmailAddress.Create($"cus7-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Guid.NewGuid(),
                contactPolicy: policy
            )
            .Value;
        await using var db = NewContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task ChangeAsync(Guid id, bool emailEnabled, bool smsEnabled)
    {
        await using var db = NewContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == id);
        Assert.True(customer.ChangeContactPolicy(emailEnabled, smsEnabled, Guid.NewGuid()).IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<(bool Email, bool Sms)> ReadAsync(Guid id)
    {
        await using var db = NewContext();
        var policy = await db
            .Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.ContactPolicy.EmailEnabled, c.ContactPolicy.SmsEnabled })
            .SingleAsync();
        return (policy.EmailEnabled, policy.SmsEnabled);
    }
}
