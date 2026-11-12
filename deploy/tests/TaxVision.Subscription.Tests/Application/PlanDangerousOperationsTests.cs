using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;
using TaxVision.Subscription.Application.Plans.Commands.PlanSales;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// D13: retirar, deprecar y archivar piden vista previa, token de confirmación y motivo, y quedan auditados.
public sealed class PlanDangerousOperationsTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly IOptions<SubscriptionOptions> Trial = Options.Create(
        new SubscriptionOptions { DefaultPlanCode = "enterprise" }
    );

    [Fact]
    public async Task The_preview_shows_the_impact_and_changes_nothing()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var subscribers = new InMemoryPlanSubscriptions(ActiveOn(pro), ActiveOn(pro));

        var preview = await Preview(pro, PlanOperation.WithdrawFromSale, subscribers, pro, enterprise);

        Assert.Equal(2, preview.OfficesOnPlan);
        Assert.Empty(preview.Blockers);
        Assert.False(string.IsNullOrWhiteSpace(preview.ConfirmationToken));
        Assert.True(pro.IsOnSale);
        Assert.Empty(pro.DomainEvents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Withdrawing_without_a_reason_is_rejected(string? reason)
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var preview = await Preview(
            pro,
            PlanOperation.WithdrawFromSale,
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );

        var result = await Withdraw(
            pro,
            preview.ConfirmationToken,
            reason,
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );

        Assert.Equal("Catalog.ReasonRequired", result.Error.Code);
        Assert.True(pro.IsOnSale);
    }

    [Fact]
    public async Task A_reason_longer_than_500_characters_is_rejected()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var preview = await Preview(
            pro,
            PlanOperation.WithdrawFromSale,
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );

        var result = await Withdraw(
            pro,
            preview.ConfirmationToken,
            new string('x', 501),
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );

        Assert.Equal("Catalog.ReasonTooLong", result.Error.Code);
    }

    [Fact]
    public async Task Withdrawing_without_the_preview_token_is_rejected()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));

        var result = await Withdraw(pro, token: null, "Single plan", new InMemoryPlanSubscriptions(), pro, enterprise);

        Assert.Equal("Catalog.ConfirmationRequired", result.Error.Code);
        Assert.True(pro.IsOnSale);
    }

    // Lo que se vio en la vista previa ya no es cierto: otra oficina se sumó al plan.
    [Fact]
    public async Task A_token_from_before_a_change_is_stale()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var subscribers = new InMemoryPlanSubscriptions(ActiveOn(pro));
        var preview = await Preview(pro, PlanOperation.WithdrawFromSale, subscribers, pro, enterprise);
        subscribers.Add(ActiveOn(pro));

        var result = await Withdraw(pro, preview.ConfirmationToken, "Single plan", subscribers, pro, enterprise);

        Assert.Equal("Catalog.ConfirmationStale", result.Error.Code);
        Assert.True(pro.IsOnSale);
    }

    [Fact]
    public async Task A_token_from_before_the_plan_changed_is_stale()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var preview = await Preview(
            pro,
            PlanOperation.WithdrawFromSale,
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );
        pro.SetSalesRank(SalesRank.Create(9).Value, Admin, DateTime.UtcNow);

        var result = await Withdraw(
            pro,
            preview.ConfirmationToken,
            "Single plan",
            new InMemoryPlanSubscriptions(),
            pro,
            enterprise
        );

        Assert.Equal("Catalog.ConfirmationStale", result.Error.Code);
    }

    [Fact]
    public async Task A_confirmed_withdrawal_is_applied_and_audited_with_its_reason()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var subscribers = new InMemoryPlanSubscriptions(ActiveOn(pro));
        var preview = await Preview(pro, PlanOperation.WithdrawFromSale, subscribers, pro, enterprise);
        var audit = new InMemoryCatalogAuditLog();

        var result = await Withdraw(
            pro,
            preview.ConfirmationToken,
            "Single plan",
            subscribers,
            [pro, enterprise],
            audit
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(pro.IsOnSale);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("plan.withdrawn_from_sale", entry.Action);
        Assert.Equal("Plan", entry.EntityType);
        Assert.Equal(pro.Id, entry.EntityId);
        Assert.Equal("pro", entry.EntityCode);
        Assert.Equal(CatalogAuditActorKind.PlatformAdmin, entry.ActorKind);
        Assert.Equal(Admin, entry.ActorUserId);
        Assert.Equal("Single plan", entry.Reason);
        Assert.Equal("test-correlation-id", entry.CorrelationId);
        Assert.Contains("\"IsOnSale\":true", entry.BeforeJson);
        Assert.Contains("\"IsOnSale\":false", entry.AfterJson);
    }

    [Fact]
    public async Task The_last_plan_on_sale_stays_blocked_even_with_a_valid_token()
    {
        var enterprise = OnSale("enterprise");
        var preview = await Preview(
            enterprise,
            PlanOperation.WithdrawFromSale,
            new InMemoryPlanSubscriptions(),
            enterprise
        );

        var result = await Withdraw(
            enterprise,
            preview.ConfirmationToken,
            "Test",
            new InMemoryPlanSubscriptions(),
            enterprise
        );

        Assert.Contains("Plan.LastOnSale", preview.Blockers);
        Assert.Equal("Plan.LastOnSale", result.Error.Code);
    }

    [Fact]
    public async Task A_confirmed_deprecation_takes_the_plan_off_sale_and_is_audited()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        var preview = await Preview(pro, PlanOperation.Deprecate, new InMemoryPlanSubscriptions(), pro, enterprise);
        var audit = new InMemoryCatalogAuditLog();

        var result = await DeprecatePlanHandler.Handle(
            new DeprecatePlanCommand(pro.Id, Admin, preview.ConfirmationToken, "End of life"),
            new InMemoryPlanRepo(pro, enterprise),
            new InMemoryPlanSubscriptions(),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            Trial,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(PlanStatus.Deprecated, pro.Status);
        Assert.False(pro.IsOnSale);
        Assert.Equal("plan.deprecated", Assert.Single(audit.Entries).Action);
    }

    // Archivar con oficinas todavía en el plan las dejaría colgadas de un plan que ya no existe para el negocio.
    [Fact]
    public async Task A_plan_with_offices_cannot_be_archived()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        pro.Deprecate(Admin, DateTime.UtcNow);
        var subscribers = new InMemoryPlanSubscriptions(ActiveOn(pro));
        var preview = await Preview(pro, PlanOperation.Archive, subscribers, pro, enterprise);

        var result = await Archive(
            pro,
            preview.ConfirmationToken,
            subscribers,
            new InMemoryCatalogAuditLog(),
            pro,
            enterprise
        );

        Assert.Contains("Catalog.PlanHasSubscribers", preview.Blockers);
        Assert.Equal("Catalog.PlanHasSubscribers", result.Error.Code);
        Assert.Equal(PlanStatus.Deprecated, pro.Status);
    }

    [Fact]
    public async Task A_deprecated_plan_without_offices_is_archived_and_audited()
    {
        var (pro, enterprise) = (OnSale("pro"), OnSale("enterprise"));
        pro.Deprecate(Admin, DateTime.UtcNow);
        var preview = await Preview(pro, PlanOperation.Archive, new InMemoryPlanSubscriptions(), pro, enterprise);
        var audit = new InMemoryCatalogAuditLog();

        var result = await Archive(
            pro,
            preview.ConfirmationToken,
            new InMemoryPlanSubscriptions(),
            audit,
            pro,
            enterprise
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(PlanStatus.Archived, pro.Status);
        Assert.Equal("plan.archived", Assert.Single(audit.Entries).Action);
    }

    private static async Task<PlanOperationPreview> Preview(
        SubscriptionPlan plan,
        PlanOperation operation,
        InMemoryPlanSubscriptions subscribers,
        params SubscriptionPlan[] catalog
    )
    {
        var result = await PreviewPlanOperationHandler.Handle(
            new PreviewPlanOperationQuery(plan.Id, operation),
            new InMemoryPlanRepo(catalog),
            subscribers,
            Trial,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static Task<BuildingBlocks.Results.Result> Withdraw(
        SubscriptionPlan plan,
        string? token,
        string? reason,
        InMemoryPlanSubscriptions subscribers,
        params SubscriptionPlan[] catalog
    ) => Withdraw(plan, token, reason, subscribers, catalog, new InMemoryCatalogAuditLog());

    private static Task<BuildingBlocks.Results.Result> Withdraw(
        SubscriptionPlan plan,
        string? token,
        string? reason,
        InMemoryPlanSubscriptions subscribers,
        SubscriptionPlan[] catalog,
        InMemoryCatalogAuditLog audit
    ) =>
        WithdrawPlanFromSaleHandler.Handle(
            new WithdrawPlanFromSaleCommand(plan.Id, Admin, token, reason),
            new InMemoryPlanRepo(catalog),
            subscribers,
            new FakeUnitOfWork(),
            Trial,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result> Archive(
        SubscriptionPlan plan,
        string token,
        InMemoryPlanSubscriptions subscribers,
        InMemoryCatalogAuditLog audit,
        params SubscriptionPlan[] catalog
    ) =>
        ArchivePlanHandler.Handle(
            new ArchivePlanCommand(plan.Id, Admin, token, "No offices left"),
            new InMemoryPlanRepo(catalog),
            subscribers,
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            Trial,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private static SubscriptionPlan OnSale(string code)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Admin, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Admin, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Admin, nowUtc);
        plan.PutOnSale(Admin, nowUtc);
        plan.ClearDomainEvents();
        return plan;
    }

    private static TenantSubscription ActiveOn(SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
