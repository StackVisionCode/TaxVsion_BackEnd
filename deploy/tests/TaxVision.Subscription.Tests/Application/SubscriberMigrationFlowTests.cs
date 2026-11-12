using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Migrations;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Subscription.Tests.Application;

// D11 de punta a punta sin base: vista previa con bloqueos, confirmación con token, ejecución por oficina y aviso.
public sealed class SubscriberMigrationFlowTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public async Task The_preview_lists_each_office_with_its_change_date_and_what_blocks_it()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var fits = s.Office();
        var tooManyUsers = s.Office();
        var tooMuchData = s.Office();
        var users = UsersOf((fits, 2), (tooManyUsers, 5), (tooMuchData, 1));
        var storage = new FakeStorageUsageClient(new() { [tooMuchData.TenantId] = 30 * GiB });

        var preview = await Planner(s, users, storage)
            .PreviewAsync(s.Plan.Id, s.To.Id, "AtNextRenewal", 30, Now, default);

        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error.Code : null);
        Assert.False(preview.Value.OnlyImprovement);
        Assert.Null(Office(preview, fits).BlockedReason);
        Assert.Equal("Migration.ExceedsSeats", Office(preview, tooManyUsers).BlockedReason);
        Assert.Equal("Migration.ExceedsStorage", Office(preview, tooMuchData).BlockedReason);
        Assert.Equal(
            RenewalSchedule.FirstRenewalOnOrAfter(fits, Now.AddDays(30)),
            Office(preview, fits).EffectiveAtUtc
        );
        Assert.Contains("price $129.00 → $69.00 per month", Office(preview, fits).Summary);
    }

    [Fact]
    public async Task Bought_seats_count_and_an_unknown_usage_blocks()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 50 * GiB);
        var withSeats = s.Office();
        var unknown = s.Office();
        var reads = new FakeSubscriberMigrationReads(new() { [withSeats.TenantId] = new OfficeHoldings(3, null) });

        var preview = await Planner(s, UsersOf((withSeats, 5)), new FakeStorageUsageClient(), reads)
            .PreviewAsync(s.Plan.Id, s.To.Id, "AtNextRenewal", 30, Now, default);

        Assert.Null(Office(preview, withSeats).BlockedReason);
        Assert.Equal("Migration.UsageUnavailable", Office(preview, unknown).BlockedReason);
    }

    // Si la edición nueva no recorta nada, no hace falta preguntarle a nadie cuánto usa.
    [Fact]
    public async Task An_only_improvement_change_needs_no_usage_lookup()
    {
        var s = Scenario(toPrice: 99m, toSeats: 12, toStorage: 60 * GiB);
        s.Office();
        var storage = new FakeStorageUsageClient();

        var preview = await Planner(s, FakeTenantUserCountClient.Unavailable(), storage)
            .PreviewAsync(s.Plan.Id, s.To.Id, "Immediate", 0, Now, default);

        Assert.True(preview.Value.OnlyImprovement);
        Assert.Null(Assert.Single(preview.Value.Offices).BlockedReason);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task Starting_needs_the_current_preview_token()
    {
        var s = Scenario(toPrice: 99m, toSeats: 12, toStorage: 60 * GiB);
        s.Office();
        var planner = Planner(s, FakeTenantUserCountClient.Unavailable(), new FakeStorageUsageClient());
        var preview = await planner.PreviewAsync(s.Plan.Id, s.To.Id, "Immediate", 0, Now, default);
        s.Office();

        var result = await Start(
            s,
            planner,
            "Immediate",
            0,
            preview.Value.PreviewToken,
            new InMemorySubscriberMigrationRepo()
        );

        Assert.Equal("Catalog.ConfirmationStale", result.Error.Code);
    }

    [Fact]
    public async Task An_immediate_move_that_worsens_any_office_is_refused()
    {
        var s = Scenario(toPrice: 149m, toSeats: 12, toStorage: 60 * GiB);
        s.Office();
        var planner = Planner(s, FakeTenantUserCountClient.Unavailable(), new FakeStorageUsageClient());
        var preview = await planner.PreviewAsync(s.Plan.Id, s.To.Id, "Immediate", 0, Now, default);

        var result = await Start(
            s,
            planner,
            "Immediate",
            0,
            preview.Value.PreviewToken,
            new InMemorySubscriberMigrationRepo()
        );

        Assert.Equal("Migration.ImmediateRequiresImprovement", result.Error.Code);
    }

    [Fact]
    public async Task Starting_records_every_office_and_audits_it()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var fits = s.Office();
        var blocked = s.Office();
        var planner = Planner(s, UsersOf((fits, 1), (blocked, 9)), new FakeStorageUsageClient());
        var preview = await planner.PreviewAsync(s.Plan.Id, s.To.Id, "AtNextRenewal", 30, Now, default);
        var repo = new InMemorySubscriberMigrationRepo();
        var audit = new InMemoryCatalogAuditLog();

        var result = await Start(s, planner, "AtNextRenewal", 30, preview.Value.PreviewToken, repo, audit);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var migration = Assert.Single(repo.Migrations);
        Assert.Equal([fits.TenantId], migration.PendingTenantIds());
        Assert.Equal("Migration.ExceedsSeats", migration.Offices.Single(o => o.TenantId == blocked.TenantId).Detail);
        Assert.Equal("subscriber_migration.started", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public void A_renewal_migration_schedules_the_office_with_its_notice()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var office = s.Office();
        var migration = Migration(s, SubscriberMigrationMode.AtNextRenewal, 30, office);

        SubscriberMigrationSteps.ApplyToOffice(migration, office, s.Plan, s.Catalog, capacityBlock: null, Now);

        Assert.Equal(s.To.Id, office.ScheduledVersionId);
        Assert.Equal(RenewalSchedule.FirstRenewalOnOrAfter(office, Now.AddDays(30)), office.ScheduledVersionAtUtc);
        Assert.Equal(SubscriberMigrationOutcome.Scheduled, migration.Offices.Single().Outcome);
        Assert.Equal(SubscriberMigrationStatus.Completed, migration.Status);
    }

    [Fact]
    public void An_immediate_migration_moves_the_office_now()
    {
        var s = Scenario(toPrice: 99m, toSeats: 12, toStorage: 60 * GiB);
        var office = s.Office();
        var migration = Migration(s, SubscriberMigrationMode.Immediate, 0, office);

        SubscriberMigrationSteps.ApplyToOffice(migration, office, s.Plan, s.Catalog, capacityBlock: null, Now);

        Assert.Equal(s.To.Id, office.PlanVersionId);
        Assert.Equal(SubscriberMigrationOutcome.Applied, migration.Offices.Single().Outcome);
    }

    [Fact]
    public void An_office_that_now_does_not_fit_is_blocked_and_left_alone()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var office = s.Office();
        var migration = Migration(s, SubscriberMigrationMode.AtNextRenewal, 30, office);

        SubscriberMigrationSteps.ApplyToOffice(migration, office, s.Plan, s.Catalog, "Migration.ExceedsSeats", Now);

        Assert.Null(office.ScheduledVersionId);
        Assert.Equal(SubscriberMigrationOutcome.Blocked, migration.Offices.Single().Outcome);
    }

    [Fact]
    public void An_office_that_changed_plan_since_the_preview_is_skipped()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var office = s.Office();
        var migration = Migration(s, SubscriberMigrationMode.AtNextRenewal, 30, office);
        var other = Scenario(toPrice: 10m, toSeats: 50, toStorage: 100 * GiB, code: "other");
        office.ChangePlan(other.Plan, other.To, null, Admin, Now);

        SubscriberMigrationSteps.ApplyToOffice(migration, office, s.Plan, s.Catalog, capacityBlock: null, Now);

        Assert.Equal(SubscriberMigrationOutcome.Skipped, migration.Offices.Single().Outcome);
        Assert.Equal("Migration.PlanChanged", migration.Offices.Single().Detail);
    }

    [Fact]
    public void Cancelling_unschedules_offices_that_were_not_applied_yet()
    {
        var s = Scenario(toPrice: 69m, toSeats: 2, toStorage: 10 * GiB);
        var office = s.Office();
        var migration = Migration(s, SubscriberMigrationMode.AtNextRenewal, 30, office);
        SubscriberMigrationSteps.ApplyToOffice(migration, office, s.Plan, s.Catalog, null, Now);
        migration.Cancel(Admin, "Changed our mind", Now);

        SubscriberMigrationSteps.UnscheduleOffice(migration, office, Now);

        Assert.Null(office.ScheduledVersionId);
        Assert.Equal(SubscriberMigrationOutcome.Unscheduled, migration.Offices.Single().Outcome);
    }

    // El aviso viaja por el canal de siempre (Auth → Notification → Scribe) con la fecha y el resumen.
    [Fact]
    public void The_scheduled_change_becomes_a_notice_for_the_office()
    {
        var tenant = Guid.NewGuid();
        var migrationId = Guid.NewGuid();
        var envelope = new Envelope { CorrelationId = "corr-7" };

        var notice = SubscriptionVersionChangeScheduledDomainEventHandler.Handle(
            new SubscriptionVersionChangeScheduledDomainEvent(
                tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "pro",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now.AddDays(40),
                migrationId,
                "price $129.00 → $69.00 per month",
                SubscriptionStatus.Active,
                Admin,
                Now
            ),
            envelope
        );

        Assert.Equal(tenant, notice.TenantId);
        Assert.Equal("VersionChangeScheduled", notice.Reason);
        Assert.Equal("Active", notice.Status);
        Assert.Equal("Active", notice.PreviousStatus);
        Assert.Equal(Now.AddDays(40), notice.VersionChangeEffectiveAtUtc);
        Assert.Equal("price $129.00 → $69.00 per month", notice.VersionChangeSummary);
        Assert.Equal(migrationId, notice.MigrationId);
        Assert.Equal("corr-7", notice.CorrelationId);
    }

    [Fact]
    public void An_applied_change_rebuilds_the_office_entitlements()
    {
        var tenant = Guid.NewGuid();

        RecalculateEntitlementsCommand command = SubscriptionVersionChangedDomainEventHandler.Handle(
            new SubscriptionVersionChangedDomainEvent(
                tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Now
            )
        );

        Assert.Equal(tenant, command.TenantId);
    }

    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriberMigrationOfficePreview Office(
        BuildingBlocks.Results.Result<SubscriberMigrationPreview> preview,
        TenantSubscription office
    ) => preview.Value.Offices.Single(o => o.TenantId == office.TenantId);

    private static FakeTenantUserCountClient UsersOf(params (TenantSubscription Office, int Users)[] offices) =>
        FakeTenantUserCountClient.PerTenant(offices.ToDictionary(o => o.Office.TenantId, o => o.Users));

    private static SubscriberMigrationPlanner Planner(
        Setup s,
        ITenantUserCountClient users,
        IStorageUsageClient storage,
        ISubscriberMigrationReads? reads = null
    ) =>
        new(
            new InMemoryPlanRepo(s.Plan),
            new FakeModuleRepo(s.Catalog),
            s.Subscribers,
            reads ?? new FakeSubscriberMigrationReads(),
            users,
            storage
        );

    private static Task<BuildingBlocks.Results.Result<Guid>> Start(
        Setup s,
        SubscriberMigrationPlanner planner,
        string mode,
        int notice,
        string token,
        InMemorySubscriberMigrationRepo repo,
        InMemoryCatalogAuditLog? audit = null
    ) =>
        StartSubscriberMigrationHandler.Handle(
            new StartSubscriberMigrationCommand(s.Plan.Id, s.To.Id, mode, notice, "Price update", token, Admin),
            planner,
            repo,
            new FakeUnitOfWork(),
            audit ?? new InMemoryCatalogAuditLog(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private static SubscriberMigration Migration(
        Setup s,
        SubscriberMigrationMode mode,
        int notice,
        TenantSubscription office
    ) =>
        SubscriberMigration
            .Start(
                s.Plan.Id,
                s.Plan.Code.Value,
                s.To.Id,
                s.To.VersionNumber,
                mode,
                notice,
                onlyImprovement: mode == SubscriberMigrationMode.Immediate,
                "Price update",
                Admin,
                [new SubscriberMigrationOfficePlan(office.TenantId, office.Id, null)],
                Now
            )
            .Value;

    private sealed class Setup(SubscriptionPlan plan, SubscriptionPlanVersion from, SubscriptionPlanVersion to)
    {
        public SubscriptionPlan Plan { get; } = plan;
        public SubscriptionPlanVersion From { get; } = from;
        public SubscriptionPlanVersion To { get; } = to;
        public InMemoryPlanSubscriptions Subscribers { get; } = new();

        public Module[] Catalog { get; } = [Module.Create("customers", "Customers", "Customers").Value];

        public TenantSubscription Office()
        {
            var office = TenantSubscription
                .ActivateImmediately(
                    Guid.NewGuid(),
                    Plan,
                    From,
                    BillingCycle.Monthly,
                    Now.AddDays(-20),
                    Now.AddDays(10),
                    Guid.Empty,
                    Now
                )
                .Value;
            office.ClearDomainEvents();
            Subscribers.Add(office);
            return office;
        }
    }

    private static Setup Scenario(decimal toPrice, int toSeats, long toStorage, string code = "pro")
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Admin, Now)
            .Value;
        var from = Edition(plan.Id, 1, 129m, seats: 10, storage: 50 * GiB);
        var to = Edition(plan.Id, 2, toPrice, toSeats, toStorage);
        plan.AddVersion(from, Admin, Now);
        plan.PublishVersion(from.Id, Now, Admin, Now);
        plan.AddVersion(to, Admin, Now);
        plan.PublishVersion(to.Id, Now, Admin, Now);
        plan.ClearDomainEvents();
        return new Setup(plan, from, to);
    }

    private static SubscriptionPlanVersion Edition(Guid planId, int number, decimal monthly, int seats, long storage)
    {
        var version = SubscriptionPlanVersion.Create(planId, number, 14, [BillingCycle.Monthly]).Value;
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    seats.ToString(),
                    "seats"
                )
                .Value
        );
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("storage.max_bytes").Value,
                    EntitlementValueType.Long,
                    storage.ToString(),
                    "storage"
                )
                .Value
        );
        version.AddFeature(
            PlanFeature.Create(version.Id, EntitlementKey.Create("module.customers").Value, true, "customers").Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthly, "USD").Value).Value
        );
        return version;
    }
}
