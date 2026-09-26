using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

namespace TaxVision.Subscription.Tests.Persistence;

/// <summary>
/// Las dos consultas del recordatorio diario son excluyentes: quien canceló al fin del período NO renueva,
/// así que no puede recibir el aviso de "se te va a cobrar" — recibe el de "tu acceso termina".
/// </summary>
public sealed class RenewalReminderQueriesTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static SubscriptionDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeTenantContext()
        );

    [Fact]
    public async Task A_scheduled_cancellation_is_reported_as_ending_and_never_as_renewing()
    {
        var databaseName = Guid.NewGuid().ToString();
        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddDays(8);

        var renewing = ActiveSubscription(nowUtc, nowUtc.AddDays(7));
        var ending = ActiveSubscription(nowUtc, nowUtc.AddDays(7));
        ending.ScheduleCancellation("too expensive", Guid.Empty, nowUtc);

        await using (var seedDb = CreateContext(databaseName))
        {
            await seedDb.Subscriptions.AddRangeAsync(renewing, ending);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new TenantSubscriptionRepository(db);

        var renewingRows = await repository.GetRenewingBetweenAsync(nowUtc, windowEndUtc, 100);
        var endingRows = await repository.GetAccessEndingBetweenAsync(nowUtc, windowEndUtc, 100);

        Assert.Equal(renewing.Id, Assert.Single(renewingRows).Id);
        Assert.Equal(ending.Id, Assert.Single(endingRows).Id);
    }

    private static TenantSubscription ActiveSubscription(DateTime nowUtc, DateTime periodEndUtc)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                periodEndUtc,
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
