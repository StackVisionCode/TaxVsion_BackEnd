using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>"A la venta", rango y destacado contra SQL Server: columnas, orden del listado y RowVersion del plan.</summary>
public sealed class PlanSalesIntegrationTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task The_seeded_catalog_is_on_sale_in_rank_order()
    {
        await using var db = SubscriptionIntegrationDb.NewContext(Guid.Empty);

        var onSale = await new PlanRepository(db).GetOnSaleAsync();

        var codes = onSale.Select(plan => plan.Code.Value).ToList();
        Assert.True(
            codes.IndexOf(PlanCatalog.Starter) < codes.IndexOf(PlanCatalog.Pro)
                && codes.IndexOf(PlanCatalog.Pro) < codes.IndexOf(PlanCatalog.Enterprise),
            string.Join(", ", codes)
        );
    }

    [Fact]
    public async Task Off_sale_plans_leave_the_list_and_rank_orders_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var low = OnSalePlan($"zlow{suffix}", rank: 900);
        var high = OnSalePlan($"zhigh{suffix}", rank: 899);
        var hidden = OnSalePlan($"zhid{suffix}", rank: 898);
        hidden.WithdrawFromSale(Actor, DateTime.UtcNow);
        await using (var seed = SubscriptionIntegrationDb.NewContext(Guid.Empty))
        {
            seed.Plans.AddRange(low, high, hidden);
            await seed.SaveChangesAsync();
        }

        await using var db = SubscriptionIntegrationDb.NewContext(Guid.Empty);
        var codes = (await new PlanRepository(db).GetOnSaleAsync()).Select(plan => plan.Code.Value).ToList();

        Assert.DoesNotContain(hidden.Code.Value, codes);
        Assert.True(codes.IndexOf(high.Code.Value) < codes.IndexOf(low.Code.Value), string.Join(", ", codes));
        var stored = await db.Plans.AsNoTracking().SingleAsync(plan => plan.Id == high.Id);
        Assert.Equal(899, stored.SalesRank.Value);
        Assert.True(stored.IsOnSale);
        Assert.NotEmpty(stored.RowVersion);
    }

    [Fact]
    public async Task Two_admins_editing_the_same_plan_conflict()
    {
        var plan = OnSalePlan($"zrow{Guid.NewGuid():N}"[..12], rank: 950);
        await using (var seed = SubscriptionIntegrationDb.NewContext(Guid.Empty))
        {
            seed.Plans.Add(plan);
            await seed.SaveChangesAsync();
        }

        await using var first = SubscriptionIntegrationDb.NewContext(Guid.Empty);
        await using var second = SubscriptionIntegrationDb.NewContext(Guid.Empty);
        var a = await first.Plans.SingleAsync(p => p.Id == plan.Id);
        var b = await second.Plans.SingleAsync(p => p.Id == plan.Id);

        Assert.True(a.SetFeatured(true, Actor, DateTime.UtcNow).IsSuccess);
        await first.SaveChangesAsync();

        Assert.True(b.SetSalesRank(SalesRank.Create(951).Value, Actor, DateTime.UtcNow).IsSuccess);
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    private static SubscriptionPlan OnSalePlan(string code, int rank)
    {
        var plan = AddOnTestCatalog.PlanWith(code, ["documents"]);
        Assert.True(plan.PutOnSale(Actor, DateTime.UtcNow).IsSuccess);
        Assert.True(plan.SetSalesRank(SalesRank.Create(rank).Value, Actor, DateTime.UtcNow).IsSuccess);
        plan.ClearDomainEvents();
        return plan;
    }
}
