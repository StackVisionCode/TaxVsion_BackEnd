using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Infrastructure.Persistence;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-8 contra SQL real: la versión del perfil se compara en memoria, así que dos eventos que cargan la misma
/// fila la comparan contra el mismo valor. El token hace que el perdedor relea y su versión vieja se ignore.
/// </summary>
public sealed class IssuerProfileConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-billing000001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set BILLING_DB_CONNECTION or the Billing user-secrets to run this test."
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private BillingDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private static TenantOfficeProfile Named(string name) => new(name, null, null, null, null, null, null);

    private Task<DomainIssuerProfile> LoadAsync(BillingDbContext db) =>
        db.IssuerProfiles.IgnoreQueryFilters().SingleAsync(p => p.TenantId == _tenantId);

    public async Task InitializeAsync()
    {
        var profile = DomainIssuerProfile.Create(_tenantId, Now);
        Assert.True(profile.ApplyTenantProfile(2, Named("v2"), Now));
        await using var db = NewContext();
        db.IssuerProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.IssuerProfiles.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task V3AndV4InParallel_V4Wins()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var newer = await LoadAsync(first);
        var older = await LoadAsync(second);

        Assert.True(newer.ApplyTenantProfile(4, Named("v4"), Now));
        await first.SaveChangesAsync();

        Assert.True(older.ApplyTenantProfile(3, Named("v3"), Now));
        try
        {
            await second.SaveChangesAsync();
        }
        catch (ConflictException)
        {
            // Lo que hace el reintento del mensaje: relee y la v3 ya no aplica.
            await using var retry = NewContext();
            Assert.False((await LoadAsync(retry)).ApplyTenantProfile(3, Named("v3"), Now));
        }

        await using var check = NewContext();
        var stored = await LoadAsync(check);
        Assert.Equal(4, stored.SourceVersion);
        Assert.Equal("v4", stored.Name);
    }
}
