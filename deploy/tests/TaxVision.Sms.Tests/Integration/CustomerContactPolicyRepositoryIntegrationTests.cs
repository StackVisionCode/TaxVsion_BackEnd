using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Domain.Customers;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>
/// Tabla de la política de contacto contra el SQL Server real (user-secrets de Sms o SMS_DB_CONNECTION).
/// Tenants propios y limpieza al terminar.
/// </summary>
public sealed class CustomerContactPolicyRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly string _connectionString =
        SmsIntegrationDatabase.ConnectionString;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private SmsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(_connectionString).Options, new NoTenant());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        Guid[] tenants = [_tenant, _otherTenant];
        await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .Where(p => tenants.Contains(p.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Sms_disabled_is_persisted_and_returned_only_for_its_tenant()
    {
        var disabled = Guid.NewGuid();
        var enabled = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            var repo = new CustomerContactPolicyRepository(seed);
            await repo.AddAsync(CustomerContactPolicyProjection.Create(_tenant, disabled, smsEnabled: false));
            await repo.AddAsync(CustomerContactPolicyProjection.Create(_tenant, enabled, smsEnabled: true));
            await repo.AddAsync(CustomerContactPolicyProjection.Create(_otherTenant, enabled, smsEnabled: false));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await new CustomerContactPolicyRepository(db).GetSmsDisabledCustomerIdsAsync(
            _tenant,
            [disabled, enabled]
        );

        Assert.Equal([disabled], result);
    }

    [Fact]
    public async Task Second_row_for_the_same_customer_is_rejected()
    {
        var customer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.CustomerContactPolicyProjections.Add(CustomerContactPolicyProjection.Create(_tenant, customer, true));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        db.CustomerContactPolicyProjections.Add(CustomerContactPolicyProjection.Create(_tenant, customer, false));

        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Older_snapshot_does_not_overwrite_a_newer_row()
    {
        var customer = Guid.NewGuid();
        var snapshotAt = DateTime.UtcNow.AddMinutes(-5);
        await using (var seed = NewContext())
        {
            seed.CustomerContactPolicyProjections.Add(CustomerContactPolicyProjection.Create(_tenant, customer, false));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var updated = await new CustomerContactPolicyRepository(db).ReconcileIfOlderAsync(
            _tenant,
            customer,
            smsEnabled: true,
            snapshotAt
        );

        Assert.False(updated);
        var row = await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.TenantId == _tenant && p.CustomerId == customer);
        Assert.False(row.SmsEnabled);
    }
}
