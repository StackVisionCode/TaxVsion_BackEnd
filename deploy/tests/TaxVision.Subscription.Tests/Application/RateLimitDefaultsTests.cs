using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.RateLimiting;
using TaxVision.Subscription.Application.RateLimiting.Commands.ReviseDefaultRateLimit;
using TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;
using TaxVision.Subscription.Application.RateLimiting.DomainEvents;
using TaxVision.Subscription.Application.RateLimiting.Queries;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Plans.Events;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// D12: un plan sin filas usa el perfil por defecto (nunca el cupo base), uno nuevo nace con sus filas copiadas, y
// bajar un cupo pide vista previa, token y motivo.
public sealed class RateLimitDefaultsTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task A_new_plan_gets_its_rows_copied_from_the_profile_and_audited()
    {
        var rows = new InMemoryPlanRateLimitRepo();
        var audit = new InMemoryCatalogAuditLog();
        var unitOfWork = new FakeUnitOfWork();

        await Provision("growth", rows, audit, unitOfWork);

        Assert.Equal(10, rows.Rows.Count);
        Assert.Equal(20m, rows.Rows.Single(row => row.Category == RateLimitCategory.K).MultiplierOverride);
        Assert.All(rows.Rows, row => Assert.Null(row.HardOverridePerMinute));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("rate_limit.provisioned", entry.Action);
        Assert.Equal("system:rate-limit-provisioning", entry.ActorLabel);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Provisioning_never_overwrites_an_existing_row_and_is_idempotent()
    {
        var rows = new InMemoryPlanRateLimitRepo(Row("growth", RateLimitCategory.F, 3m));
        var audit = new InMemoryCatalogAuditLog();

        await Provision("growth", rows, audit, new FakeUnitOfWork());
        await Provision("growth", rows, audit, new FakeUnitOfWork());

        Assert.Equal(10, rows.Rows.Count);
        Assert.Equal(3m, rows.Rows.Single(row => row.Category == RateLimitCategory.F).MultiplierOverride);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task The_effective_catalog_completes_missing_rows_with_the_profile()
    {
        var growth = Plan("growth");
        var pro = Plan("pro");
        var rows = new InMemoryPlanRateLimitRepo(Row("pro", RateLimitCategory.F, 5m));

        var catalog = await EffectivePlanRateLimits.BuildAsync(
            new InMemoryPlanRepo(growth, pro),
            rows,
            new InMemoryRateLimitProfileRepo(RateLimitDefaultProfile.Seed(DateTime.UtcNow)),
            CancellationToken.None
        );

        Assert.Equal(20, catalog.Count);
        var proF = catalog.Single(entry => entry.PlanCode == "pro" && entry.Category == RateLimitCategory.F);
        Assert.Equal(5m, proF.Multiplier);
        Assert.False(proF.FromDefaultProfile);
        var proH = catalog.Single(entry => entry.PlanCode == "pro" && entry.Category == RateLimitCategory.H);
        Assert.Equal(10m, proH.Multiplier);
        Assert.True(proH.FromDefaultProfile);
        Assert.Equal(20m, catalog.Single(e => e.PlanCode == "growth" && e.Category == RateLimitCategory.K).Multiplier);
    }

    // Lo que leen la flota (HttpPlanRateLimitReader) y Communication: ya viene completo.
    [Fact]
    public async Task The_internal_endpoint_returns_the_completed_catalog()
    {
        var result = await GetPlanRateLimitsHandler.Handle(
            new GetPlanRateLimitsQuery(),
            new InMemoryPlanRepo(Plan("growth")),
            new InMemoryPlanRateLimitRepo(),
            new InMemoryRateLimitProfileRepo(RateLimitDefaultProfile.Seed(DateTime.UtcNow)),
            CancellationToken.None
        );

        Assert.Equal(10, result.Value.Count);
        Assert.All(result.Value, row => Assert.Equal("growth", row.PlanCode));
        Assert.Equal(10m, result.Value.Single(row => row.Category == "F").MultiplierOverride);
    }

    [Fact]
    public async Task Raising_a_profile_multiplier_is_applied_directly_and_audited()
    {
        var setup = DefaultSetup();

        var result = await ReviseDefault(setup, "G", 12m, token: null, reason: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(12m, setup.Profile.MultiplierFor(RateLimitCategory.G).Value);
        Assert.Equal("rate_limit.default_revised", Assert.Single(setup.Audit.Entries).Action);
    }

    [Theory]
    [InlineData(null, "Lower")]
    [InlineData("token", null)]
    public async Task Lowering_a_profile_multiplier_needs_the_token_and_a_reason(string? token, string? reason)
    {
        var setup = DefaultSetup();

        var result = await ReviseDefault(setup, "G", 5m, token, reason);

        Assert.Equal(reason is null ? "Catalog.ReasonRequired" : "Catalog.ConfirmationRequired", result.Error.Code);
        Assert.Equal(10m, setup.Profile.MultiplierFor(RateLimitCategory.G).Value);
    }

    [Fact]
    public async Task The_profile_preview_counts_offices_on_plans_that_rely_on_it()
    {
        var setup = DefaultSetup();
        setup.Subscribers.Add(ActiveOn(setup.Growth));
        setup.Subscribers.Add(ActiveOn(setup.Growth));
        setup.Subscribers.Add(ActiveOn(setup.Pro));

        var preview = await PreviewDefault(setup, "G", 5m);

        // pro tiene su propia fila G: no depende del perfil.
        Assert.Equal(2, preview.OfficesAffected);
        Assert.True(preview.IsDecrease);
        Assert.Equal(10m, preview.CurrentMultiplier);
    }

    [Fact]
    public async Task A_confirmed_profile_decrease_is_applied_with_its_reason()
    {
        var setup = DefaultSetup();
        var preview = await PreviewDefault(setup, "G", 5m);

        var result = await ReviseDefault(setup, "G", 5m, preview.ConfirmationToken, "Abuse on read endpoints");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(5m, setup.Profile.MultiplierFor(RateLimitCategory.G).Value);
        Assert.Equal("Abuse on read endpoints", Assert.Single(setup.Audit.Entries).Reason);
    }

    [Fact]
    public async Task A_profile_token_from_before_another_office_joined_is_stale()
    {
        var setup = DefaultSetup();
        var preview = await PreviewDefault(setup, "G", 5m);
        setup.Subscribers.Add(ActiveOn(setup.Growth));

        var result = await ReviseDefault(setup, "G", 5m, preview.ConfirmationToken, "Lower");

        Assert.Equal("Catalog.ConfirmationStale", result.Error.Code);
    }

    [Fact]
    public async Task Lowering_a_plan_row_needs_the_preview_and_counts_its_offices()
    {
        var setup = DefaultSetup();
        setup.Subscribers.Add(ActiveOn(setup.Pro));

        var withoutToken = await UpsertPlanRow(setup, "pro", "G", 2m, hard: null, token: null, reason: "Lower");
        var preview = await PreviewPlanRow(setup, "pro", "G", 2m, hard: null);
        var confirmed = await UpsertPlanRow(setup, "pro", "G", 2m, hard: null, preview.ConfirmationToken, "Lower");

        Assert.Equal("Catalog.ConfirmationRequired", withoutToken.Error.Code);
        Assert.Equal(1, preview.OfficesAffected);
        Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.Error.Code : null);
        Assert.Equal(
            2m,
            setup
                .Rows.Rows.Single(r => r.PlanCode.Value == "pro" && r.Category == RateLimitCategory.G)
                .MultiplierOverride
        );
    }

    [Fact]
    public async Task Raising_a_plan_row_is_applied_directly()
    {
        var setup = DefaultSetup();

        var result = await UpsertPlanRow(setup, "pro", "G", 6m, hard: null, token: null, reason: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    // Un cupo fijo reemplaza al multiplicador y puede quedar por debajo del cupo base: se trata como una baja.
    [Fact]
    public async Task Setting_a_hard_override_is_treated_as_a_decrease()
    {
        var setup = DefaultSetup();

        var result = await UpsertPlanRow(setup, "pro", "G", 5m, hard: 30, token: null, reason: "Negotiated cap");
        var preview = await PreviewPlanRow(setup, "pro", "G", 5m, hard: 30);

        Assert.Equal("Catalog.ConfirmationRequired", result.Error.Code);
        Assert.True(preview.IsDecrease);
    }

    [Fact]
    public async Task A_plan_row_below_the_base_is_rejected_before_any_preview()
    {
        var setup = DefaultSetup();

        var preview = await PreviewPlanRateLimitChangeHandler.Handle(
            new PreviewPlanRateLimitChangeQuery("pro", "G", 0.5m, null),
            new InMemoryRateLimitProfileRepo(setup.Profile),
            new InMemoryPlanRepo(setup.Growth, setup.Pro),
            setup.Rows,
            setup.Subscribers,
            CancellationToken.None
        );
        var result = await UpsertPlanRow(setup, "pro", "G", 0.5m, hard: null, "any", "Lower");

        Assert.Equal("RateLimit.MultiplierBelowBase", preview.Error.Code);
        Assert.Equal("RateLimit.MultiplierBelowBase", result.Error.Code);
    }

    private sealed record Setup(
        RateLimitDefaultProfile Profile,
        SubscriptionPlan Growth,
        SubscriptionPlan Pro,
        InMemoryPlanRateLimitRepo Rows,
        InMemoryPlanSubscriptions Subscribers,
        InMemoryCatalogAuditLog Audit
    );

    private static Setup DefaultSetup() =>
        new(
            RateLimitDefaultProfile.Seed(DateTime.UtcNow),
            Plan("growth"),
            Plan("pro"),
            new InMemoryPlanRateLimitRepo(Row("pro", RateLimitCategory.G, 5m)),
            new InMemoryPlanSubscriptions(),
            new InMemoryCatalogAuditLog()
        );

    private static Task Provision(
        string planCode,
        InMemoryPlanRateLimitRepo rows,
        InMemoryCatalogAuditLog audit,
        FakeUnitOfWork unitOfWork
    ) =>
        PlanCreatedDomainEventHandler.Handle(
            new PlanCreatedDomainEvent(Guid.NewGuid(), planCode, Admin, DateTime.UtcNow),
            new InMemoryRateLimitProfileRepo(RateLimitDefaultProfile.Seed(DateTime.UtcNow)),
            rows,
            unitOfWork,
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private static async Task<RateLimitChangePreview> PreviewDefault(Setup setup, string category, decimal multiplier)
    {
        var result = await PreviewDefaultRateLimitChangeHandler.Handle(
            new PreviewDefaultRateLimitChangeQuery(category, multiplier),
            new InMemoryRateLimitProfileRepo(setup.Profile),
            new InMemoryPlanRepo(setup.Growth, setup.Pro),
            setup.Rows,
            setup.Subscribers,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static Task<BuildingBlocks.Results.Result> ReviseDefault(
        Setup setup,
        string category,
        decimal multiplier,
        string? token,
        string? reason
    ) =>
        ReviseDefaultRateLimitHandler.Handle(
            new ReviseDefaultRateLimitCommand(category, multiplier, Admin, token, reason),
            new InMemoryRateLimitProfileRepo(setup.Profile),
            new InMemoryPlanRepo(setup.Growth, setup.Pro),
            setup.Rows,
            setup.Subscribers,
            new FakeUnitOfWork(),
            setup.Audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private static async Task<RateLimitChangePreview> PreviewPlanRow(
        Setup setup,
        string planCode,
        string category,
        decimal multiplier,
        int? hard
    )
    {
        var result = await PreviewPlanRateLimitChangeHandler.Handle(
            new PreviewPlanRateLimitChangeQuery(planCode, category, multiplier, hard),
            new InMemoryRateLimitProfileRepo(setup.Profile),
            new InMemoryPlanRepo(setup.Growth, setup.Pro),
            setup.Rows,
            setup.Subscribers,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static Task<BuildingBlocks.Results.Result> UpsertPlanRow(
        Setup setup,
        string planCode,
        string category,
        decimal multiplier,
        int? hard,
        string? token,
        string? reason
    ) =>
        UpsertPlanRateLimitHandler.Handle(
            new UpsertPlanRateLimitCommand(planCode, category, multiplier, hard, Admin, token, reason),
            setup.Rows,
            new InMemoryRateLimitProfileRepo(setup.Profile),
            new InMemoryPlanRepo(setup.Growth, setup.Pro),
            setup.Subscribers,
            new FakeUnitOfWork(),
            setup.Audit,
            new FakeCorrelationContext(),
            NullLogger<PlanRateLimit>.Instance,
            CancellationToken.None
        );

    private static PlanRateLimit Row(string planCode, RateLimitCategory category, decimal multiplier) =>
        PlanRateLimit.Seed(Guid.NewGuid(), PlanCode.Create(planCode).Value, category, multiplier).Value;

    private static SubscriptionPlan Plan(string code)
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
