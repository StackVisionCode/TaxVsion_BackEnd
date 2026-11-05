using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de Sms o SMS_DB_CONNECTION): la baja se decide por
/// (tenant, número), sin importar el cliente. Tenants propios y limpieza al terminar.
/// </summary>
public sealed class SmsOptOutRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant1 = Guid.NewGuid();
    private readonly Guid _tenant2 = Guid.NewGuid();
    private readonly string _phone = "+1555010" + Random.Shared.Next(1000, 9999);
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("SMS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("b8e2f1a4-7c3d-4e9a-9f21-3c5d8a1b6e42")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set SMS_DB_CONNECTION or the Sms user-secrets to run this test.");

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
        Guid[] tenants = [_tenant1, _tenant2];
        await db.SmsOptOuts.IgnoreQueryFilters().Where(o => tenants.Contains(o.TenantId)).ExecuteDeleteAsync();
        await db.SmsMessages.IgnoreQueryFilters().Where(m => tenants.Contains(m.TenantId)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task IsPhoneOptedOut_ignores_customer_and_respects_tenant()
    {
        var phone = PhoneE164.Create(_phone).Value;
        await using (var seed = NewContext())
        {
            var optedOut = SmsOptOut.CreateSubscribed(_tenant1, Guid.NewGuid(), phone, DateTime.UtcNow);
            optedOut.OptOut("STOP", DateTime.UtcNow);
            seed.SmsOptOuts.Add(optedOut);
            seed.SmsOptOuts.Add(SmsOptOut.CreateSubscribed(_tenant2, Guid.NewGuid(), phone, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var repo = new SmsOptOutRepository(db);

        Assert.True(await repo.IsPhoneOptedOutAsync(_tenant1, _phone, CancellationToken.None));
        Assert.False(await repo.IsPhoneOptedOutAsync(_tenant2, _phone, CancellationToken.None));
    }

    [Fact]
    public async Task ListTenantsByPhone_returns_each_tenant_with_its_latest_customer()
    {
        var phone = PhoneE164.Create(_phone).Value;
        var oldCustomer = Guid.NewGuid();
        var latestCustomer = Guid.NewGuid();
        var otherTenantCustomer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            var now = DateTime.UtcNow;
            seed.SmsMessages.Add(NewMessage(_tenant1, oldCustomer, phone, now.AddMinutes(-10)));
            seed.SmsMessages.Add(NewMessage(_tenant1, latestCustomer, phone, now));
            seed.SmsMessages.Add(NewMessage(_tenant2, otherTenantCustomer, phone, now.AddMinutes(-5)));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var tenants = await new SmsMessageRepository(db).ListTenantsByPhoneAsync(_phone, CancellationToken.None);

        Assert.Contains(tenants, t => t.TenantId == _tenant1 && t.CustomerId == latestCustomer);
        Assert.Contains(tenants, t => t.TenantId == _tenant2 && t.CustomerId == otherTenantCustomer);
        Assert.Equal(2, tenants.Count(t => t.TenantId == _tenant1 || t.TenantId == _tenant2));
    }

    private static SmsMessage NewMessage(Guid tenantId, Guid customerId, PhoneE164 phone, DateTime createdAtUtc) =>
        SmsMessage
            .Create(
                tenantId,
                customerId,
                phone,
                SmsBody.Create("integration test").Value,
                "it-" + Guid.NewGuid().ToString("N"),
                "it-corr",
                Guid.NewGuid(),
                "fake",
                "integration",
                [],
                createdAtUtc
            )
            .Value;
}
