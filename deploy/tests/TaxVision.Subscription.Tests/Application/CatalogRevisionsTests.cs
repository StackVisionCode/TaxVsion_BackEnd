using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Catalog.Revisions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// R1 re-ancla sin cambiar términos; R2 deja a la venta solo Enterprise con los términos nuevos. Ambas idempotentes.
public sealed class CatalogRevisionsTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void R1_runs_before_R2_and_R3_goes_last()
    {
        Assert.Equal(
            [
                "2026-11-11-r1-rebaseline",
                "2026-11-11-r2-single-enterprise",
                "2026-11-11-r3-plan-descriptions",
                "2026-11-13-r4-financial-insights",
                "2026-11-13-financial-insights-legacy-editions",
            ],
            CatalogRevisionCatalog.All.Select(revision => revision.Id)
        );
    }

    // Financial Insights entra a Enterprise y Pro (también a quien ya es cliente, porque solo suma) y se vende
    // como add-on a los demás.
    [Fact]
    public async Task Financial_insights_enters_enterprise_and_pro_and_re_anchors_their_offices()
    {
        var (starter, pro, enterprise, _) = TodaysCatalog();
        var proOffice = ActiveOn(pro, pro.GetPublishedVersion()!);
        var enterpriseOffice = ActiveOn(enterprise, enterprise.GetPublishedVersion()!);
        var starterOffice = ActiveOn(starter, starter.GetPublishedVersion()!);
        var modules = ModuleCatalog();

        var result = await Apply(
            new FinancialInsightsModuleRevision(),
            [starter, pro, enterprise],
            [proOffice, enterpriseOffice, starterOffice],
            modules: modules
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        foreach (var plan in new[] { pro, enterprise })
        {
            var published = plan.GetPublishedVersion()!;
            Assert.Contains(
                "financial_insights",
                ContractedEntitlementPolicy.EnabledModules(published, modules.All).Value
            );
            // Lo demás de la edición se conserva.
            Assert.Contains("meetings", ContractedEntitlementPolicy.EnabledModules(published, modules.All).Value);
        }
        Assert.Equal(pro.GetPublishedVersion()!.Id, proOffice.PlanVersionId);
        Assert.Equal(enterprise.GetPublishedVersion()!.Id, enterpriseOffice.PlanVersionId);
        Assert.DoesNotContain(
            "financial_insights",
            ContractedEntitlementPolicy.EnabledModules(starter.GetPublishedVersion()!, modules.All).Value
        );

        var addon = modules.All.Single(module => module.Code == "financial_insights");
        Assert.True(addon.IsSellableAsAddon);
        Assert.Equal(29m, addon.AddonPriceMonthlyUsd);
        Assert.Equal(290m, addon.AddonPriceYearlyUsd);
    }

    [Fact]
    public async Task Financial_insights_module_revision_is_idempotent()
    {
        var (starter, pro, enterprise, _) = TodaysCatalog();
        var modules = ModuleCatalog();
        await Apply(new FinancialInsightsModuleRevision(), [starter, pro, enterprise], [], modules: modules);
        var proEdition = pro.GetPublishedVersion()!.Id;

        var again = await Apply(new FinancialInsightsModuleRevision(), [starter, pro, enterprise], [], modules: modules);

        Assert.True(again.IsSuccess);
        Assert.Equal(proEdition, pro.GetPublishedVersion()!.Id);
    }

    // Solo mejora: si la edición de la oficina tiene un módulo que la nueva no trae, no se la mueve.
    [Fact]
    public async Task Financial_insights_never_moves_an_office_that_would_lose_a_module()
    {
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB);
        var richer = PublishCopy(pro, 129m, 1290m, seats: 10, storage: 50 * GiB, extraModule: "campaigns");
        var office = ActiveOn(pro, richer);
        PublishCopy(pro, 129m, 1290m, seats: 10, storage: 50 * GiB, extraModule: "meetings");
        var enterprise = Plan("enterprise", 99m, 999m, seats: 3, storage: 15 * GiB);

        var result = await Apply(new FinancialInsightsModuleRevision(), [pro, enterprise], [office], modules: ModuleCatalog());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(richer.Id, office.PlanVersionId);
        Assert.Contains("skipped 1", result.Value);
    }

    // Edición heredada: copia una edición superada con el módulo nuevo, mismos términos, y nunca se vende.
    [Fact]
    public void A_legacy_edition_keeps_the_terms_of_its_source_adds_the_module_and_is_never_published()
    {
        var enterprise = Plan("enterprise", 299m, 2990m, seats: 25, storage: 200 * GiB);
        var old = enterprise.GetPublishedVersion()!;
        var current = PublishCopy(enterprise, 99m, 999m, seats: 3, storage: 15 * GiB, extraModule: "meetings");

        var legacy = enterprise.AddLegacyEditionWithModules(old.Id, ["financial_insights"], Guid.Empty, DateTime.UtcNow);

        Assert.True(legacy.IsSuccess, legacy.IsFailure ? legacy.Error.Code : null);
        Assert.Equal(PlanVersionStatus.Superseded, legacy.Value.Status);
        Assert.Equal(CommercialTerms.Of(old), CommercialTerms.Of(legacy.Value));
        Assert.Equal(current.Id, enterprise.GetPublishedVersion()!.Id);
        var modules = ContractedEntitlementPolicy.EnabledModules(legacy.Value, ModuleCatalog().All).Value;
        Assert.Contains("financial_insights", modules);
        Assert.Contains("meetings", modules);
    }

    [Fact]
    public void A_legacy_edition_only_comes_from_a_superseded_edition()
    {
        var enterprise = Plan("enterprise", 99m, 999m, seats: 3, storage: 15 * GiB);

        var fromPublished = enterprise.AddLegacyEditionWithModules(
            enterprise.GetPublishedVersion()!.Id,
            ["financial_insights"],
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(fromPublished.IsFailure);
        Assert.Equal("Plan.LegacyEditionSourceNotSuperseded", fromPublished.Error.Code);
    }

    // Toda oficina de Enterprise o Pro sin el módulo lo recibe, conservando su precio y sus límites.
    [Fact]
    public async Task Offices_on_old_editions_get_financial_insights_without_changing_their_terms()
    {
        var modules = ModuleCatalog();
        var enterprise = Plan("enterprise", 299m, 2990m, seats: 25, storage: 200 * GiB);
        var old = enterprise.GetPublishedVersion()!;
        var office = ActiveOn(enterprise, old);
        PublishCopy(enterprise, 99m, 999m, seats: 3, storage: 15 * GiB, extraModule: "meetings");
        var starter = Plan("starter", 39m, 390m, seats: 1, storage: 5 * GiB);
        var starterOffice = ActiveOn(starter, starter.GetPublishedVersion()!);
        await Apply(new FinancialInsightsModuleRevision(), [enterprise, starter], [office, starterOffice], modules: modules);
        var published = enterprise.GetPublishedVersion()!.Id;

        var result = await Apply(
            new FinancialInsightsLegacyEditionsRevision(),
            [enterprise, starter],
            [office, starterOffice],
            modules: modules
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var edition = enterprise.Versions.Single(version => version.Id == office.PlanVersionId);
        Assert.Equal(CommercialTerms.Of(old), CommercialTerms.Of(edition));
        Assert.Contains("financial_insights", ContractedEntitlementPolicy.EnabledModules(edition, modules.All).Value);
        Assert.Equal(published, enterprise.GetPublishedVersion()!.Id);
        Assert.Equal(starter.GetPublishedVersion()!.Id, starterOffice.PlanVersionId);

        // Idempotente: una segunda pasada no crea otra edición ni mueve a nadie.
        var editions = enterprise.Versions.Count;
        var again = await Apply(
            new FinancialInsightsLegacyEditionsRevision(),
            [enterprise, starter],
            [office, starterOffice],
            modules: modules
        );
        Assert.True(again.IsSuccess);
        Assert.Equal(editions, enterprise.Versions.Count);
        Assert.Equal(edition.Id, office.PlanVersionId);
    }

    [Fact]
    public void The_monthly_document_quota_of_financial_insights_has_a_platform_default()
    {
        var seed = Assert.Single(
            EntitlementKeyCatalog.PlatformDefaults,
            item => item.Key == FinancialInsightsEntitlementCatalog.DocumentsPerMonth
        );
        Assert.Equal("financial.documents_per_month", seed.Key);
        Assert.Equal(EntitlementValueType.Int, seed.ValueType);
        Assert.Equal("200", seed.DefaultValue);
    }

    // R3: la descripción sembrada decía "25 usuarios" y estaba en español; la edición manda en las cifras.
    [Fact]
    public async Task R3_replaces_the_old_seeded_descriptions_and_keeps_an_edited_one()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();
        starter.UpdateMetadata(
            "Starter",
            "Para oficinas que estan empezando: 3 usuarios, clientes, firmas y documentos.",
            Guid.Empty,
            DateTime.UtcNow
        );
        pro.UpdateMetadata("Pro", "Edited by the platform admin.", Guid.Empty, DateTime.UtcNow);
        enterprise.UpdateMetadata(
            "Enterprise",
            "Para multiservices con equipos grandes: 25 usuarios y todos los modulos.",
            Guid.Empty,
            DateTime.UtcNow
        );
        var audit = new InMemoryCatalogAuditLog();

        var result = await Apply(
            new R3PlanDescriptionsRevision(),
            [starter, pro, enterprise, testlive],
            [],
            catalogAudit: audit
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(R3PlanDescriptionsRevision.DescriptionFor("starter"), starter.Description);
        Assert.Equal(R3PlanDescriptionsRevision.DescriptionFor("enterprise"), enterprise.Description);
        Assert.Equal("Edited by the platform admin.", pro.Description);
        Assert.Equal("testlive plan", testlive.Description);
        Assert.DoesNotContain("25", enterprise.Description);
        Assert.Equal(2, audit.Entries.Count);

        // Idempotente: la segunda pasada no audita ni cambia nada.
        var secondAudit = new InMemoryCatalogAuditLog();
        var again = await Apply(
            new R3PlanDescriptionsRevision(),
            [starter, pro, enterprise, testlive],
            [],
            catalogAudit: secondAudit
        );
        Assert.True(again.IsSuccess);
        Assert.Empty(secondAudit.Entries);
        Assert.Equal("Edited by the platform admin.", pro.Description);
    }

    // Una base nueva siembra ya el texto de R3: la revisión no tiene nada que cambiar.
    [Fact]
    public void The_seeder_descriptions_are_the_R3_ones()
    {
        foreach (var code in new[] { "starter", "pro", "enterprise" })
        {
            var description = R3PlanDescriptionsRevision.DescriptionFor(code);
            Assert.False(string.IsNullOrWhiteSpace(description));
            Assert.DoesNotMatch(@"\d", description);
        }
        Assert.Equal(
            R3PlanDescriptionsRevision.DescriptionFor("enterprise"),
            TaxVision.Subscription.Infrastructure.Persistence.SubscriptionPlanCatalogSeeder.SeedDescriptionFor(
                "enterprise"
            )
        );
    }

    [Fact]
    public async Task R1_re_anchors_offices_on_an_old_edition_with_the_same_terms()
    {
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB);
        var old = pro.GetPublishedVersion()!;
        var first = ActiveOn(pro, old);
        var second = ActiveOn(pro, old);
        var current = PublishCopy(pro, 129m, 1290m, seats: 10, storage: 50 * GiB, extraModule: "meetings");

        var result = await Apply(new R1RebaselineRevision(), [pro], [first, second]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(current.Id, first.PlanVersionId);
        Assert.Equal(current.Id, second.PlanVersionId);
        Assert.Contains("rebaselined 2", result.Value);
    }

    // Cada oficina re-anclada lo ve en su propio historial: misma edición comercial, otra ancla, quién y por qué.
    [Fact]
    public async Task R1_leaves_an_entry_in_the_audit_of_each_re_anchored_office()
    {
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB);
        var old = pro.GetPublishedVersion()!;
        var office = ActiveOn(pro, old);
        var current = PublishCopy(pro, 129m, 1290m, seats: 10, storage: 50 * GiB, extraModule: "meetings");
        var tenantAudit = new FakeSubscriptionAuditLogWriter();

        await Apply(new R1RebaselineRevision(), [pro], [office], tenantAudit: tenantAudit);

        var entry = Assert.Single(tenantAudit.Entries);
        Assert.Equal(office.TenantId, entry.TenantId);
        Assert.Equal("subscription.version_rebaselined", entry.Action);
        Assert.Equal("System", entry.ActorType);
        Assert.Equal("deploy:2026-11-11-r1-rebaseline", entry.Reason);
        Assert.Equal("deploy:2026-11-11-r1-rebaseline", entry.CorrelationId);
        Assert.Contains(old.Id.ToString(), entry.BeforePayload);
        Assert.Contains(current.Id.ToString(), entry.AfterPayload);
    }

    [Fact]
    public async Task R2_audits_each_plan_it_changes_as_the_deploy()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();
        var audit = new InMemoryCatalogAuditLog();

        await Apply(new R2SingleEnterpriseRevision(), [starter, pro, enterprise, testlive], [], catalogAudit: audit);

        Assert.Equal(
            ["enterprise", "pro", "starter", "testlive"],
            audit.Entries.Select(entry => entry.EntityCode).Order()
        );
        Assert.All(
            audit.Entries,
            entry =>
            {
                Assert.Equal(CatalogAuditActorKind.Deploy, entry.ActorKind);
                Assert.Equal("deploy:2026-11-11-r2-single-enterprise", entry.ActorLabel);
                Assert.Equal("catalog.revision", entry.Action);
            }
        );
        Assert.Contains("\"IsOnSale\":true", audit.Entries.Single(e => e.EntityCode == "pro").BeforeJson);
        Assert.Contains("\"IsOnSale\":false", audit.Entries.Single(e => e.EntityCode == "pro").AfterJson);
    }

    [Fact]
    public async Task R2_a_second_time_audits_nothing()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();
        SubscriptionPlan[] plans = [starter, pro, enterprise, testlive];
        await Apply(new R2SingleEnterpriseRevision(), plans, []);
        var audit = new InMemoryCatalogAuditLog();

        await Apply(new R2SingleEnterpriseRevision(), plans, [], catalogAudit: audit);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task R1_leaves_offices_whose_terms_differ_and_reports_them()
    {
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB);
        var old = pro.GetPublishedVersion()!;
        var office = ActiveOn(pro, old);
        PublishCopy(pro, 149m, 1490m, seats: 10, storage: 50 * GiB);

        var result = await Apply(new R1RebaselineRevision(), [pro], [office]);

        Assert.True(result.IsSuccess);
        Assert.Equal(old.Id, office.PlanVersionId);
        Assert.Contains("skipped 1", result.Value);
        Assert.Contains(office.TenantId.ToString(), result.Value);
    }

    [Fact]
    public async Task R1_does_nothing_to_offices_already_on_the_published_edition()
    {
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB);
        var office = ActiveOn(pro, pro.GetPublishedVersion()!);

        var result = await Apply(new R1RebaselineRevision(), [pro], [office]);

        Assert.Contains("rebaselined 0", result.Value);
        Assert.Empty(office.DomainEvents);
    }

    [Fact]
    public async Task R2_leaves_only_enterprise_on_sale_with_the_new_terms()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();

        var result = await Apply(new R2SingleEnterpriseRevision(), [starter, pro, enterprise, testlive], []);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        AssertPlan(enterprise, 99m, 999m, seats: 3, storage: 15 * GiB, onSale: true, rank: 3, featured: true);
        AssertPlan(pro, 69m, 699m, seats: 2, storage: 10 * GiB, onSale: false, rank: 2, featured: false);
        AssertPlan(starter, 39m, 399m, seats: 1, storage: 5 * GiB, onSale: false, rank: 1, featured: false);
        Assert.False(testlive.IsOnSale);
        // Lo que el pedido no menciona se copia tal cual (D10).
        Assert.Equal("40", Value(enterprise.GetPublishedVersion()!, "invitations.max_pending"));
        Assert.Contains("module.meetings", enterprise.GetPublishedVersion()!.Features.Select(f => f.FeatureKey.Value));
    }

    [Fact]
    public async Task R2_twice_creates_no_extra_editions()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();
        SubscriptionPlan[] plans = [starter, pro, enterprise, testlive];
        await Apply(new R2SingleEnterpriseRevision(), plans, []);
        var versions = plans.Sum(plan => plan.Versions.Count);

        var again = await Apply(new R2SingleEnterpriseRevision(), plans, []);

        Assert.True(again.IsSuccess);
        Assert.Equal(versions, plans.Sum(plan => plan.Versions.Count));
    }

    // El trial de las oficinas nuevas tiene que ser un plan a la venta: con otro, R2 se niega y no toca nada.
    [Fact]
    public async Task R2_refuses_when_the_trial_plan_is_not_enterprise()
    {
        var (starter, pro, enterprise, testlive) = TodaysCatalog();

        var result = await Apply(
            new R2SingleEnterpriseRevision(),
            [starter, pro, enterprise, testlive],
            [],
            trialPlan: "starter"
        );

        Assert.Equal("CatalogRevision.TrialPlanMustBeEnterprise", result.Error.Code);
        Assert.Single(enterprise.Versions);
        Assert.True(starter.IsOnSale);
    }

    [Fact]
    public async Task R2_fails_when_a_catalog_plan_is_missing()
    {
        var (starter, pro, _, _) = TodaysCatalog();

        var result = await Apply(new R2SingleEnterpriseRevision(), [starter, pro], []);

        Assert.Equal("CatalogRevision.PlanMissing", result.Error.Code);
    }

    [Fact]
    public async Task The_follow_up_recalculates_once_and_announces_each_pending_revision()
    {
        var now = DateTime.UtcNow;
        var store = new InMemoryCatalogRevisionStore(
            AppliedCatalogRevision.Create("r1", "rebaselined 2", now.AddSeconds(-2)),
            AppliedCatalogRevision.Create("r2", "single enterprise", now.AddSeconds(-1))
        );
        var bus = new CapturingMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await PublishCatalogRevisionFollowUpsHandler.Handle(
            new PublishCatalogRevisionFollowUpsCommand(),
            store,
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.Single(bus.Published.OfType<RecalculateEntitlementsForAllPlansCommand>());
        Assert.Equal(
            ["r1", "r2"],
            bus.Published.OfType<CatalogChangedIntegrationEvent>().Select(notice => notice.EntityCode)
        );
        Assert.All(store.Rows, row => Assert.NotNull(row.FollowUpPublishedAtUtc));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task The_follow_up_with_nothing_pending_publishes_nothing()
    {
        var bus = new CapturingMessageBus();

        await PublishCatalogRevisionFollowUpsHandler.Handle(
            new PublishCatalogRevisionFollowUpsCommand(),
            new InMemoryCatalogRevisionStore(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result<string>> Apply(
        ICatalogRevision revision,
        SubscriptionPlan[] plans,
        TenantSubscription[] subscriptions,
        string trialPlan = "enterprise",
        InMemoryCatalogAuditLog? catalogAudit = null,
        FakeSubscriptionAuditLogWriter? tenantAudit = null,
        ModuleCatalogFixture? modules = null
    ) =>
        revision.ApplyAsync(
            new CatalogRevisionContext(
                new InMemoryPlanRepo(plans),
                new InMemoryPlanSubscriptions(subscriptions),
                new SubscriptionOptions { DefaultPlanCode = trialPlan },
                catalogAudit ?? new InMemoryCatalogAuditLog(),
                tenantAudit ?? new FakeSubscriptionAuditLogWriter(),
                CatalogAuditActor.Deploy(revision.Id),
                DateTime.UtcNow
            )
            {
                Modules = modules?.Repository,
            },
            CancellationToken.None
        );

    public sealed record ModuleCatalogFixture(IReadOnlyCollection<Module> All, FakeModuleRepo Repository);

    // Catálogo como el de hoy: Financial Insights comercial y todavía sin precio de add-on.
    private static ModuleCatalogFixture ModuleCatalog()
    {
        Module Core(string code) => Module.Create(code, code, code).Value;
        Module Commercial(string code, decimal? monthly)
        {
            var module = Module.Create(code, code, code).Value;
            module.MarkAsCommercial();
            if (monthly is { } price)
                module.SetAsAddon(price, price * 10);
            return module;
        }

        Module[] all =
        [
            Core("customers"),
            Commercial("meetings", 29m),
            Commercial("campaigns", 29m),
            Commercial("email", 29m),
            Commercial("financial_insights", null),
        ];
        return new ModuleCatalogFixture(all, new FakeModuleRepo(all));
    }

    private static (
        SubscriptionPlan Starter,
        SubscriptionPlan Pro,
        SubscriptionPlan Enterprise,
        SubscriptionPlan Testlive
    ) TodaysCatalog()
    {
        var starter = Plan("starter", 49m, 490m, seats: 3, storage: 10 * GiB, rank: 1, invitations: 5);
        var pro = Plan("pro", 129m, 1290m, seats: 10, storage: 50 * GiB, rank: 2, invitations: 15, featured: true);
        var enterprise = Plan("enterprise", 299m, 2990m, seats: 25, storage: 200 * GiB, rank: 3, invitations: 40);
        var testlive = Plan("testlive", 10m, 100m, seats: 1, storage: GiB, rank: 100, invitations: 1);
        return (starter, pro, enterprise, testlive);
    }

    private static void AssertPlan(
        SubscriptionPlan plan,
        decimal monthly,
        decimal yearly,
        int seats,
        long storage,
        bool onSale,
        int rank,
        bool featured
    )
    {
        var terms = CommercialTerms.Of(plan.GetPublishedVersion()!);
        Assert.Equal(Money.Create(monthly, "USD").Value, terms.Prices[BillingCycle.Monthly]);
        Assert.Equal(Money.Create(yearly, "USD").Value, terms.Prices[BillingCycle.Yearly]);
        Assert.Equal(seats, terms.SeatsMax);
        Assert.Equal(storage, terms.StorageMaxBytes);
        Assert.Equal(onSale, plan.IsOnSale);
        Assert.Equal(rank, plan.SalesRank.Value);
        Assert.Equal(featured, plan.IsFeatured);
    }

    private static string? Value(SubscriptionPlanVersion version, string key) =>
        version.Entitlements.FirstOrDefault(entitlement => entitlement.Key.Value == key)?.DefaultValue;

    private static SubscriptionPlan Plan(
        string code,
        decimal monthly,
        decimal yearly,
        int seats,
        long storage,
        int rank = 100,
        int invitations = 5,
        bool featured = false
    )
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Guid.Empty, nowUtc)
            .Value;
        var version = Edition(plan.Id, 1, monthly, yearly, seats, storage, invitations, extraModule: "meetings");
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        plan.PutOnSale(Guid.Empty, nowUtc);
        plan.SetSalesRank(SalesRank.Create(rank).Value, Guid.Empty, nowUtc);
        plan.SetFeatured(featured, Guid.Empty, nowUtc);
        plan.ClearDomainEvents();
        return plan;
    }

    private static SubscriptionPlanVersion PublishCopy(
        SubscriptionPlan plan,
        decimal monthly,
        decimal yearly,
        int seats,
        long storage,
        string? extraModule = null
    )
    {
        var nowUtc = DateTime.UtcNow;
        var next = plan.Versions.Max(version => version.VersionNumber) + 1;
        var version = Edition(plan.Id, next, monthly, yearly, seats, storage, 5, extraModule);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return version;
    }

    private static SubscriptionPlanVersion Edition(
        Guid planId,
        int number,
        decimal monthly,
        decimal yearly,
        int seats,
        long storage,
        int invitations,
        string? extraModule
    )
    {
        var version = SubscriptionPlanVersion
            .Create(planId, number, 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        Entitlement(version, "seats.max", EntitlementValueType.Int, seats.ToString());
        Entitlement(version, "storage.max_bytes", EntitlementValueType.Long, storage.ToString());
        Entitlement(version, "invitations.max_pending", EntitlementValueType.Int, invitations.ToString());
        foreach (var module in extraModule is null ? ["customers"] : new[] { "customers", extraModule })
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthly, "USD").Value).Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Yearly, 1, null, Money.Create(yearly, "USD").Value).Value
        );
        return version;
    }

    private static void Entitlement(
        SubscriptionPlanVersion version,
        string key,
        EntitlementValueType type,
        string value
    ) =>
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition.Create(version.Id, EntitlementKey.Create(key).Value, type, value, key).Value
        );

    private static TenantSubscription ActiveOn(SubscriptionPlan plan, SubscriptionPlanVersion version)
    {
        var nowUtc = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
        subscription.ClearDomainEvents();
        return subscription;
    }
}
