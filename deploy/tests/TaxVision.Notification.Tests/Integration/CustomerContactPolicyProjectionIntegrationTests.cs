using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// Proyección de la política de contacto contra el SQL Server real (user-secrets del Api o
/// NOTIFICATION_DB_CONNECTION). Tenant propio y limpieza al terminar.
/// </summary>
public sealed class CustomerContactPolicyProjectionIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("f7c2d8e1-3b5a-4c97-8d1e-notification1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set NOTIFICATION_DB_CONNECTION or the Notification user-secrets.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private NotificationDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenant)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Row_inserted_with_both_channels_off_persists_them_off()
    {
        var customer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.CustomerContactPolicyProjections.Add(
                CustomerContactPolicyProjection.Create(_tenant, customer, emailEnabled: false, smsEnabled: false)
            );
            await seed.SaveChangesAsync();
        }

        Assert.Equal((false, false), await ReadAsync(customer));
    }

    [Fact]
    public async Task Row_reconciled_off_and_back_on_persists_both_changes()
    {
        var customer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.CustomerContactPolicyProjections.Add(
                CustomerContactPolicyProjection.Create(_tenant, customer, emailEnabled: true, smsEnabled: true)
            );
            await seed.SaveChangesAsync();
        }

        await ReconcileAsync(customer, emailEnabled: false, smsEnabled: false);
        Assert.Equal((false, false), await ReadAsync(customer));

        await ReconcileAsync(customer, emailEnabled: true, smsEnabled: true);
        Assert.Equal((true, true), await ReadAsync(customer));
    }

    private async Task ReconcileAsync(Guid customer, bool emailEnabled, bool smsEnabled)
    {
        await using var db = NewContext();
        var row = await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .SingleAsync(p => p.TenantId == _tenant && p.CustomerId == customer);
        row.Reconcile(emailEnabled, smsEnabled);
        await db.SaveChangesAsync();
    }

    private async Task<(bool Email, bool Sms)> ReadAsync(Guid customer)
    {
        await using var db = NewContext();
        var row = await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.TenantId == _tenant && p.CustomerId == customer);
        return (row.EmailEnabled, row.SmsEnabled);
    }
}
