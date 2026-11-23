using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Entitlements.Overrides;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

public sealed class TenantEntitlementOverrideAdminTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Granting_saves_audits_and_queues_a_recalculation_of_that_office()
    {
        var office = new Office();

        var result = await office.SetAsync("storage.max_bytes", "53687091200", expiresAtUtc: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("53687091200", result.Value.Value);
        Assert.Equal("Long", result.Value.ValueType);
        Assert.True(result.Value.InForce);
        Assert.Single(office.Overrides.Items);
        Assert.Equal(1, office.UnitOfWork.SaveChangesCallCount);
        var entry = Assert.Single(office.Audit.Entries);
        Assert.Equal("TenantEntitlementOverride.Granted", entry.Action);
        Assert.Equal("Acuerdo", entry.Reason);
        var queued = Assert.Single(office.Bus.Published.OfType<RecalculateEntitlementsCommand>());
        Assert.Equal(Tenant, queued.TenantId);
    }

    [Fact]
    public async Task Setting_a_key_twice_changes_the_same_row()
    {
        var office = new Office();
        await office.SetAsync("seats.max", "10", null);

        var result = await office.SetAsync("seats.max", "15", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("15", Assert.Single(office.Overrides.Items).Value);
        Assert.Equal("TenantEntitlementOverride.Changed", office.Audit.Entries[^1].Action);
        Assert.NotNull(office.Audit.Entries[^1].BeforePayload);
    }

    [Fact]
    public async Task Commercial_module_is_a_bool()
    {
        var result = await new Office().SetAsync("module.signature", "true", null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("Bool", result.Value.ValueType);
    }

    [Theory]
    [InlineData("does.not.exist")]
    [InlineData("module.unknown")]
    [InlineData("module.customers")] // núcleo: siempre encendido, no se toca
    [InlineData("Not A Key")]
    public async Task Key_outside_the_catalog_is_rejected_and_nothing_is_saved(string key)
    {
        var office = new Office();

        var result = await office.SetAsync(key, "1", null);

        Assert.Equal("EntitlementOverride.KeyNotOverridable", result.Error.Code);
        Assert.Empty(office.Overrides.Items);
        Assert.Equal(0, office.UnitOfWork.SaveChangesCallCount);
        Assert.Empty(office.Bus.Published);
    }

    [Fact]
    public async Task Value_is_checked_against_the_type_of_the_key()
    {
        var result = await new Office().SetAsync("storage.max_bytes", "1.5", null);

        Assert.Equal("EntitlementOverride.InvalidValue", result.Error.Code);
    }

    [Fact]
    public async Task Office_without_subscription_is_rejected()
    {
        var office = new Office(withSubscription: false);

        var result = await office.SetAsync("seats.max", "10", null);

        Assert.Equal("Subscription.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Removing_drops_the_row_audits_and_recalculates()
    {
        var office = new Office();
        await office.SetAsync("seats.max", "10", null);

        var removed = await office.RemoveAsync("seats.max");

        Assert.True(removed.IsSuccess);
        Assert.Empty(office.Overrides.Items);
        Assert.Equal("TenantEntitlementOverride.Removed", office.Audit.Entries[^1].Action);
        Assert.Equal(2, office.Bus.Published.OfType<RecalculateEntitlementsCommand>().Count());
    }

    [Fact]
    public async Task Removing_a_key_that_has_no_override_is_not_found()
    {
        var removed = await new Office().RemoveAsync("seats.max");

        Assert.Equal("EntitlementOverride.NotFound", removed.Error.Code);
    }

    [Fact]
    public async Task Listing_shows_whether_each_override_is_still_in_force()
    {
        var grantedAt = DateTime.UtcNow.AddDays(-2);
        var expired = TenantEntitlementOverride
            .Grant(
                Tenant,
                EntitlementKey.Create("seats.max").Value,
                EntitlementValueType.Int,
                "10",
                "r",
                grantedAt.AddDays(1),
                Admin,
                grantedAt
            )
            .Value;
        var office = new Office(expired);
        await office.SetAsync("storage.max_bytes", "1", null);

        var listed = await GetTenantEntitlementOverridesHandler.Handle(
            new GetTenantEntitlementOverridesQuery(Tenant),
            office.Overrides,
            CancellationToken.None
        );

        Assert.Equal(["seats.max:False", "storage.max_bytes:True"], listed.Value.Select(v => $"{v.Key}:{v.InForce}"));
    }

    private sealed class Office
    {
        private readonly FakeSubscriptionRepo _subscriptions;
        private readonly FakeModuleRepo _modules = new(AddOnTestCatalog.Modules(["customers"], ["signature"]));

        public Office(params TenantEntitlementOverride[] existing)
            : this(withSubscription: true, existing) { }

        public Office(bool withSubscription, params TenantEntitlementOverride[] existing)
        {
            var plan = AddOnTestCatalog.PlanWith("enterprise", ["customers"]);
            _subscriptions = new FakeSubscriptionRepo(
                withSubscription ? AddOnTestCatalog.ActiveSubscription(Tenant, plan) : null
            );
            Overrides = new FakeEntitlementOverrideRepo(existing);
        }

        public FakeEntitlementOverrideRepo Overrides { get; }
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeSubscriptionAuditLogWriter Audit { get; } = new();
        public CapturingMessageBus Bus { get; } = new();

        public Task<BuildingBlocks.Results.Result<TenantEntitlementOverrideView>> SetAsync(
            string key,
            string value,
            DateTime? expiresAtUtc
        ) =>
            SetTenantEntitlementOverrideHandler.Handle(
                new SetTenantEntitlementOverrideCommand(Tenant, key, value, "Acuerdo", expiresAtUtc, Admin),
                _subscriptions,
                _modules,
                Overrides,
                UnitOfWork,
                Audit,
                new FakeCorrelationContext(),
                Bus,
                NullLogger<TenantEntitlementOverride>.Instance,
                CancellationToken.None
            );

        public Task<BuildingBlocks.Results.Result> RemoveAsync(string key) =>
            RemoveTenantEntitlementOverrideHandler.Handle(
                new RemoveTenantEntitlementOverrideCommand(Tenant, key, "Fin del acuerdo", Admin),
                Overrides,
                UnitOfWork,
                Audit,
                new FakeCorrelationContext(),
                Bus,
                NullLogger<TenantEntitlementOverride>.Instance,
                CancellationToken.None
            );
    }
}
