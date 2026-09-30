using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// C6 — no cobrar por algo que el plan ya trae ni por el mismo add-on dos veces. Es la aceptación de la fase
/// y la contracara de la elegibilidad que el Account pinta.
/// </summary>
public sealed class PurchaseAddOnGuardsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task An_add_on_whose_module_the_plan_already_includes_cannot_be_bought()
    {
        var plan = AddOnTestCatalog.PlanWith("enterprise", modules: ["email"]);
        var definition = AddOnTestCatalog.ModuleAddOn("email.addon", "email");
        var addOns = new FakeTenantAddOnRepo();

        var result = await PurchaseAsync(plan, definition, addOns);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.AlreadyIncludedInPlan", result.Error.Code);
        Assert.Empty(addOns.Added);
    }

    [Fact]
    public async Task The_same_add_on_cannot_be_bought_twice()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", modules: ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("email.addon", "email");
        var addOns = new FakeTenantAddOnRepo([AddOnTestCatalog.Owned(TenantId, definition)]);

        var result = await PurchaseAsync(plan, definition, addOns);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.AlreadyActive", result.Error.Code);
        Assert.Empty(addOns.Added);
    }

    // Un add-on que el catálogo declara repetible (p. ej. paquetes de cupos) sí se puede sumar otra vez.
    [Fact]
    public async Task An_add_on_that_allows_several_instances_can_be_bought_again()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", modules: ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("storage.pack", module: null, allowMultiple: true);
        var addOns = new FakeTenantAddOnRepo([AddOnTestCatalog.Owned(TenantId, definition)]);

        var result = await PurchaseAsync(plan, definition, addOns);

        Assert.True(result.IsSuccess);
        Assert.Single(addOns.Added);
    }

    [Fact]
    public async Task An_add_on_the_plan_does_not_cover_is_purchased()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", modules: ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("email.addon", "email");
        var addOns = new FakeTenantAddOnRepo();

        var result = await PurchaseAsync(plan, definition, addOns);

        Assert.True(result.IsSuccess);
        Assert.Single(addOns.Added);
    }

    private static Task<Result<Guid>> PurchaseAsync(
        SubscriptionPlan plan,
        AddOnDefinition definition,
        FakeTenantAddOnRepo addOns
    )
    {
        var subscription = AddOnTestCatalog.ActiveSubscription(TenantId, plan);

        return PurchaseAddOnHandler.Handle(
            new PurchaseAddOnCommand(TenantId, definition.Code.Value, Quantity: 1, AutoRenew: true, Guid.NewGuid()),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeAddOnDefinitionRepository(definition),
            new NoSettingsRepo(),
            addOns,
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );
    }

    private sealed class NoSettingsRepo : ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
