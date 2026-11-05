using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SQL Server real (SUBSCRIPTION_DB_CONNECTION o user-secrets de Subscription). Cada prueba usa un tenant
/// propio y borra solo sus filas al terminar. No arranca el host: así no corren los jobs hospedados.
/// </summary>
internal sealed class SubscriptionIntegrationDb
{
    private static readonly Lazy<string> ConnectionString = new(() =>
        Environment.GetEnvironmentVariable("SUBSCRIPTION_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-subscription1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set SUBSCRIPTION_DB_CONNECTION or the Subscription user-secrets to run this test."
        )
    );

    public Guid TenantId { get; } = Guid.NewGuid();

    public static string Connection => ConnectionString.Value;

    public SubscriptionDbContext NewContext() => NewContext(TenantId);

    public static SubscriptionDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(Connection).Options,
            new FixedTenant(tenantId)
        );

    // Suscripción activa sobre un plan ya existente en la base (FK a SubscriptionPlans); no toca el catálogo.
    public async Task<TenantSubscription> SeedActiveSubscriptionAsync()
    {
        await using var db = NewContext();
        var plan = await db
            .Plans.AsNoTracking()
            .Include(p => p.Versions)
            .Where(p => p.Versions.Any())
            .OrderBy(p => p.Id)
            .FirstAsync();
        var nowUtc = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                TenantId,
                plan,
                plan.Versions.First(),
                BillingCycle.Monthly,
                nowUtc.AddDays(-1),
                nowUtc.AddDays(29),
                Guid.Empty,
                nowUtc
            )
            .Value;
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    public async Task CleanupAsync()
    {
        await using var db = NewContext();
        await db.AuditLogs.Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db.Seats.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db.TenantAddOns.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db.SeatPurchaseIntents.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db.AddOnPurchaseIntents.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db
            .SubscriptionRenewalIntents.IgnoreQueryFilters()
            .Where(x => x.TenantId == TenantId)
            .ExecuteDeleteAsync();
        await db.TenantSettings.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
        await db.Subscriptions.IgnoreQueryFilters().Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
