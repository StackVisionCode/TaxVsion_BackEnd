using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Application.Plans.Commands.PlanSales;
using TaxVision.Subscription.Application.Plans.DomainEvents;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Plans.Events;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Subscription.Tests.Application;

// Los handlers solo cargan, piden al plan, auditan y guardan; el aviso del catálogo sale de los eventos de dominio.
public sealed class PlanSalesHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Putting_a_plan_on_sale_saves_it_and_audits_it()
    {
        var plan = PublishedPlan("enterprise");
        var unitOfWork = new FakeUnitOfWork();
        var audit = new InMemoryCatalogAuditLog();

        var result = await PutPlanOnSaleHandler.Handle(
            new PutPlanOnSaleCommand(plan.Id, Actor),
            new InMemoryPlanRepo(plan),
            unitOfWork,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(plan.IsOnSale);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("plan.put_on_sale", entry.Action);
        Assert.Equal(Actor, entry.ActorUserId);
    }

    [Fact]
    public async Task An_unknown_plan_is_not_found()
    {
        var result = await PutPlanOnSaleHandler.Handle(
            new PutPlanOnSaleCommand(Guid.NewGuid(), Actor),
            new InMemoryPlanRepo(),
            new FakeUnitOfWork(),
            new InMemoryCatalogAuditLog(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("Plan.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task The_trial_plan_cannot_be_withdrawn()
    {
        var enterprise = OnSalePlan("enterprise");
        var pro = OnSalePlan("pro");
        var options = Options.Create(new SubscriptionOptions { DefaultPlanCode = "enterprise" });
        var preview = await PreviewPlanOperationHandler.Handle(
            new PreviewPlanOperationQuery(enterprise.Id, PlanOperation.WithdrawFromSale),
            new InMemoryPlanRepo(enterprise, pro),
            new InMemoryPlanSubscriptions(),
            options,
            CancellationToken.None
        );

        var result = await WithdrawPlanFromSaleHandler.Handle(
            new WithdrawPlanFromSaleCommand(enterprise.Id, Actor, preview.Value.ConfirmationToken, "Test"),
            new InMemoryPlanRepo(enterprise, pro),
            new InMemoryPlanSubscriptions(),
            new FakeUnitOfWork(),
            options,
            new InMemoryCatalogAuditLog(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Contains("Plan.IsTrialPlan", preview.Value.Blockers);
        Assert.Equal("Plan.IsTrialPlan", result.Error.Code);
    }

    [Fact]
    public async Task An_invalid_rank_is_rejected_without_saving()
    {
        var plan = OnSalePlan("enterprise");
        var unitOfWork = new FakeUnitOfWork();
        var audit = new InMemoryCatalogAuditLog();

        var result = await SetPlanSalesRankHandler.Handle(
            new SetPlanSalesRankCommand(plan.Id, 0, Actor),
            new InMemoryPlanRepo(plan),
            unitOfWork,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("SalesRank.Invalid", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Rank_and_featured_are_saved_and_audited()
    {
        var plan = OnSalePlan("enterprise");
        var repo = new InMemoryPlanRepo(plan);
        var audit = new InMemoryCatalogAuditLog();

        var rank = await SetPlanSalesRankHandler.Handle(
            new SetPlanSalesRankCommand(plan.Id, 5, Actor),
            repo,
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        var featured = await SetPlanFeaturedHandler.Handle(
            new SetPlanFeaturedCommand(plan.Id, true, Actor),
            repo,
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(rank.IsSuccess);
        Assert.True(featured.IsSuccess);
        Assert.Equal(5, plan.SalesRank.Value);
        Assert.True(plan.IsFeatured);
        Assert.Equal(["plan.rank", "plan.featured"], audit.Entries.Select(entry => entry.Action));
        Assert.Contains("\"Rank\":5", audit.Entries[0].AfterJson);
    }

    // Un cambio que no cambia nada no deja ruido en la auditoría.
    [Fact]
    public async Task Setting_the_same_rank_again_is_not_audited()
    {
        var plan = OnSalePlan("enterprise");
        var audit = new InMemoryCatalogAuditLog();

        await SetPlanSalesRankHandler.Handle(
            new SetPlanSalesRankCommand(plan.Id, plan.SalesRank.Value, Actor),
            new InMemoryPlanRepo(plan),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public void Each_sales_event_becomes_a_catalog_changed_notice()
    {
        var planId = Guid.NewGuid();
        var envelope = new Envelope { CorrelationId = "corr-1" };
        var now = DateTime.UtcNow;

        CatalogChangedIntegrationEvent[] notices =
        [
            PlanPutOnSaleDomainEventHandler.Handle(new PlanPutOnSaleDomainEvent(planId, "pro", Actor, now), envelope),
            PlanWithdrawnFromSaleDomainEventHandler.Handle(
                new PlanWithdrawnFromSaleDomainEvent(planId, "pro", Actor, now),
                envelope
            ),
            PlanSalesRankChangedDomainEventHandler.Handle(
                new PlanSalesRankChangedDomainEvent(planId, "pro", 100, 2, Actor, now),
                envelope
            ),
            PlanFeaturedChangedDomainEventHandler.Handle(
                new PlanFeaturedChangedDomainEvent(planId, "pro", true, Actor, now),
                envelope
            ),
        ];

        Assert.Equal(
            ["plan.put_on_sale", "plan.withdrawn_from_sale", "plan.rank", "plan.featured"],
            notices.Select(n => n.ChangeKind)
        );
        Assert.All(
            notices,
            n =>
            {
                Assert.Equal(planId, n.EntityId);
                Assert.Equal("pro", n.EntityCode);
                Assert.Equal("corr-1", n.CorrelationId);
            }
        );
    }

    private static SubscriptionPlan PublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Actor, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Actor, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Actor, DateTime.UtcNow);
        return plan;
    }

    private static SubscriptionPlan OnSalePlan(string code)
    {
        var plan = PublishedPlan(code);
        plan.PutOnSale(Actor, DateTime.UtcNow);
        plan.ClearDomainEvents();
        return plan;
    }
}
