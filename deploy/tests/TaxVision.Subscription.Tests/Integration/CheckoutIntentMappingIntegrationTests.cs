using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-10, en una base temporal: <c>Money</c> se lee y se escribe en las mismas columnas de siempre.</summary>
public sealed class CheckoutIntentMappingIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subsub10_";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString = new SqlConnectionStringBuilder(SubscriptionIntegrationDb.Connection)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task MoneyMapsToExistingColumns()
    {
        var nowUtc = DateTime.UtcNow;
        var unitPrice = Money.Create(15m, "USD").Value;
        var seat = SeatPurchaseIntent
            .Create(
                _tenantId,
                SeatType.Standard,
                2,
                true,
                unitPrice,
                BillingCycle.Monthly,
                Money.FromCents(3000, "USD"),
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        var addOn = AddOnPurchaseIntent
            .Create(
                _tenantId,
                AddOnTestCatalog.QuotaAddOn("test.pack"),
                1,
                true,
                unitPrice,
                BillingCycle.Monthly,
                Money.FromCents(1234, "USD"),
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        var renewal = SubscriptionRenewalIntent
            .Create(_tenantId, Money.FromCents(4900, "usd"), BillingCycle.Monthly, Guid.NewGuid(), nowUtc)
            .Value;
        await using (var writing = NewContext())
        {
            writing.AddRange(seat, addOn, renewal);
            await writing.SaveChangesAsync();
        }

        await using var db = NewContext();

        Assert.Equal(
            "3000|USD",
            await ReadAsync(
                db,
                "SELECT CONCAT([ProratedTotalCents], '|', [Currency]) AS [Value] FROM [SeatPurchaseIntents]"
            )
        );
        Assert.Equal(
            "1234|USD",
            await ReadAsync(
                db,
                "SELECT CONCAT([ProratedTotalCents], '|', [Currency]) AS [Value] FROM [AddOnPurchaseIntents]"
            )
        );
        // Lo guardado se devuelve igual: el value object no normaliza la moneda de filas existentes.
        Assert.Equal(
            "4900|usd",
            await ReadAsync(
                db,
                "SELECT CONCAT([AmountCents], '|', [Currency]) AS [Value] FROM [SubscriptionRenewalIntents]"
            )
        );

        var seatRead = await db.SeatPurchaseIntents.SingleAsync();
        var addOnRead = await db.AddOnPurchaseIntents.SingleAsync();
        var renewalRead = await db.SubscriptionRenewalIntents.SingleAsync();
        Assert.Equal(Money.FromCents(3000, "USD"), seatRead.ProratedTotal);
        Assert.Equal(1234, addOnRead.ProratedTotal.Cents);
        Assert.Equal("usd", renewalRead.Amount.Currency);
    }

    private static Task<string> ReadAsync(SubscriptionDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).SingleAsync();

    private SubscriptionDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
