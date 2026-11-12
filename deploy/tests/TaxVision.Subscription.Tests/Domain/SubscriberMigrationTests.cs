using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

// D11: mover clientes a otra edición es una acción explícita. Ya, solo si es pura mejora; si no, en la renovación
// con 30+ días de aviso. Las oficinas que no entran no se tocan.
public sealed class SubscriberMigrationTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Module[] Catalog =
    [
        Module.Create("customers", "Customers", "Customers").Value,
        Commercial("meetings"),
        Commercial("email"),
    ];

    // ---- Comparación de términos ----

    [Fact]
    public void A_cheaper_edition_with_more_seats_and_the_same_modules_is_only_improvement()
    {
        var (_, from, to) = TwoEditions(fromPrice: 129m, toPrice: 99m, fromSeats: 10, toSeats: 12);

        var comparison = VersionChangeComparison.Compare(from, to, BillingCycle.Monthly, Catalog);

        Assert.True(comparison.IsOnlyImprovement);
        Assert.Equal(12900, comparison.PriceFromCents);
        Assert.Equal(9900, comparison.PriceToCents);
    }

    [Theory]
    [InlineData(129, 149, 10, 10, false)]
    [InlineData(129, 99, 10, 3, false)]
    public void A_higher_price_or_fewer_seats_is_not_only_improvement(
        int fromPrice,
        int toPrice,
        int fromSeats,
        int toSeats,
        bool expected
    )
    {
        var (_, from, to) = TwoEditions(fromPrice, toPrice, fromSeats, toSeats);

        Assert.Equal(
            expected,
            VersionChangeComparison.Compare(from, to, BillingCycle.Monthly, Catalog).IsOnlyImprovement
        );
    }

    [Fact]
    public void Losing_a_module_is_not_only_improvement()
    {
        var (_, from, to) = TwoEditions(129m, 99m, 10, 10, fromModules: ["meetings"], toModules: []);

        var comparison = VersionChangeComparison.Compare(from, to, BillingCycle.Monthly, Catalog);

        Assert.False(comparison.IsOnlyImprovement);
        Assert.Equal(["meetings"], comparison.ModulesLost);
    }

    [Fact]
    public void An_edition_without_the_office_cycle_is_flagged()
    {
        var (_, from, to) = TwoEditions(129m, 99m, 10, 10, toCycles: [BillingCycle.Monthly]);

        var comparison = VersionChangeComparison.Compare(from, to, BillingCycle.Yearly, Catalog);

        Assert.False(comparison.CycleAvailable);
        Assert.False(comparison.IsOnlyImprovement);
    }

    // ---- Fecha efectiva ----

    [Fact]
    public void The_change_lands_on_the_first_renewal_after_the_notice()
    {
        var (plan, from, _) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, periodEnd: Now.AddDays(10));

        var effective = RenewalSchedule.FirstRenewalOnOrAfter(subscription, Now.AddDays(30));

        Assert.Equal(Now.AddDays(10).AddMonths(1), effective);
    }

    // ---- TenantSubscription ----

    [Fact]
    public void Scheduling_a_change_keeps_the_edition_and_announces_it()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));
        var migrationId = Guid.NewGuid();

        var result = subscription.ScheduleVersionChange(
            plan,
            to.Id,
            Now.AddDays(40),
            migrationId,
            "Price $129 → $99",
            Admin,
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(from.Id, subscription.PlanVersionId);
        Assert.Equal(to.Id, subscription.ScheduledVersionId);
        Assert.Equal(Now.AddDays(40), subscription.ScheduledVersionAtUtc);
        var evt = Assert.IsType<SubscriptionVersionChangeScheduledDomainEvent>(
            Assert.Single(subscription.DomainEvents)
        );
        Assert.Equal("Price $129 → $99", evt.ChangeSummary);
        Assert.Equal(migrationId, evt.MigrationId);
    }

    [Fact]
    public void Scheduling_twice_for_the_same_migration_does_nothing_more()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));
        var migrationId = Guid.NewGuid();
        subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), migrationId, "x", Admin, Now);
        subscription.ClearDomainEvents();

        var again = subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), migrationId, "x", Admin, Now);

        Assert.True(again.IsSuccess);
        Assert.Empty(subscription.DomainEvents);
    }

    [Fact]
    public void Another_migration_cannot_overwrite_a_scheduled_change()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));
        subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), Guid.NewGuid(), "x", Admin, Now);

        var other = subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), Guid.NewGuid(), "x", Admin, Now);

        Assert.Equal("Subscription.VersionChangeAlreadyScheduled", other.Error.Code);
    }

    [Fact]
    public void A_scheduled_change_applies_only_once_its_date_arrives()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));
        subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), Guid.NewGuid(), "x", Admin, Now);
        subscription.ClearDomainEvents();

        var early = subscription.ApplyScheduledVersionChangeIfDue(Now.AddDays(39));
        var due = subscription.ApplyScheduledVersionChangeIfDue(Now.AddDays(40));

        Assert.False(early.Value);
        Assert.True(due.Value);
        Assert.Equal(to.Id, subscription.PlanVersionId);
        Assert.Null(subscription.ScheduledVersionId);
        Assert.IsType<SubscriptionVersionChangedDomainEvent>(Assert.Single(subscription.DomainEvents));
    }

    [Fact]
    public void A_change_applied_now_moves_the_office_and_announces_it()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));

        var result = subscription.ApplyVersionChangeNow(plan, to.Id, Guid.NewGuid(), Admin, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(to.Id, subscription.PlanVersionId);
        Assert.IsType<SubscriptionVersionChangedDomainEvent>(Assert.Single(subscription.DomainEvents));
    }

    [Fact]
    public void Cancelling_removes_only_the_change_of_that_migration()
    {
        var (plan, from, to) = TwoEditions(129m, 99m, 10, 10);
        var subscription = ActiveOn(plan, from, Now.AddDays(10));
        var migrationId = Guid.NewGuid();
        subscription.ScheduleVersionChange(plan, to.Id, Now.AddDays(40), migrationId, "x", Admin, Now);

        subscription.CancelScheduledVersionChange(Guid.NewGuid(), Admin, Now);
        Assert.Equal(to.Id, subscription.ScheduledVersionId);

        subscription.CancelScheduledVersionChange(migrationId, Admin, Now);
        Assert.Null(subscription.ScheduledVersionId);
        Assert.Equal(from.Id, subscription.PlanVersionId);
    }

    // ---- SubscriberMigration ----

    [Fact]
    public void A_renewal_migration_needs_30_days_of_notice()
    {
        var result = SubscriberMigration.Start(
            Guid.NewGuid(),
            "pro",
            Guid.NewGuid(),
            2,
            SubscriberMigrationMode.AtNextRenewal,
            noticeDays: 29,
            onlyImprovement: false,
            "Price update",
            Admin,
            [Office(blockedReason: null)],
            Now
        );

        Assert.Equal("Migration.NoticeTooShort", result.Error.Code);
    }

    [Fact]
    public void An_immediate_migration_must_be_only_improvement()
    {
        var result = SubscriberMigration.Start(
            Guid.NewGuid(),
            "pro",
            Guid.NewGuid(),
            2,
            SubscriberMigrationMode.Immediate,
            noticeDays: 0,
            onlyImprovement: false,
            "Price update",
            Admin,
            [Office(blockedReason: null)],
            Now
        );

        Assert.Equal("Migration.ImmediateRequiresImprovement", result.Error.Code);
    }

    [Fact]
    public void A_migration_needs_a_reason()
    {
        var result = SubscriberMigration.Start(
            Guid.NewGuid(),
            "pro",
            Guid.NewGuid(),
            2,
            SubscriberMigrationMode.AtNextRenewal,
            noticeDays: 30,
            onlyImprovement: false,
            " ",
            Admin,
            [Office(blockedReason: null)],
            Now
        );

        Assert.Equal("Catalog.ReasonRequired", result.Error.Code);
    }

    [Fact]
    public void Blocked_offices_are_never_touched_and_the_migration_completes_when_none_is_pending()
    {
        var pending = Office(blockedReason: null);
        var blocked = Office(blockedReason: "Migration.ExceedsSeats");
        var migration = SubscriberMigration
            .Start(
                Guid.NewGuid(),
                "pro",
                Guid.NewGuid(),
                2,
                SubscriberMigrationMode.AtNextRenewal,
                30,
                onlyImprovement: false,
                "Price update",
                Admin,
                [pending, blocked],
                Now
            )
            .Value;

        Assert.Equal(SubscriberMigrationStatus.Running, migration.Status);
        Assert.Equal([pending.TenantId], migration.PendingTenantIds());

        migration.MarkScheduled(pending.TenantId, Now.AddDays(40), Now);

        Assert.Equal(SubscriberMigrationStatus.Completed, migration.Status);
        Assert.Equal(1, migration.Offices.Count(o => o.Outcome == SubscriberMigrationOutcome.Blocked));
        Assert.Equal(1, migration.Offices.Count(o => o.Outcome == SubscriberMigrationOutcome.Scheduled));
    }

    [Fact]
    public void Cancelling_leaves_scheduled_offices_to_unschedule_and_an_immediate_one_cannot_be_cancelled()
    {
        var office = Office(blockedReason: null);
        var renewal = SubscriberMigration
            .Start(
                Guid.NewGuid(),
                "pro",
                Guid.NewGuid(),
                2,
                SubscriberMigrationMode.AtNextRenewal,
                30,
                false,
                "x",
                Admin,
                [office],
                Now
            )
            .Value;
        renewal.MarkScheduled(office.TenantId, Now.AddDays(40), Now);
        var immediate = SubscriberMigration
            .Start(
                Guid.NewGuid(),
                "pro",
                Guid.NewGuid(),
                2,
                SubscriberMigrationMode.Immediate,
                0,
                true,
                "x",
                Admin,
                [Office(null)],
                Now
            )
            .Value;

        var cancelled = renewal.Cancel(Admin, "Changed our mind", Now);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(SubscriberMigrationStatus.Cancelled, renewal.Status);
        Assert.Equal([office.TenantId], renewal.ScheduledTenantIds());
        renewal.MarkUnscheduled(office.TenantId, Now);
        Assert.Empty(renewal.ScheduledTenantIds());
        Assert.Equal("Migration.CannotCancel", immediate.Cancel(Admin, "x", Now).Error.Code);
    }

    private static SubscriberMigrationOfficePlan Office(string? blockedReason) =>
        new(Guid.NewGuid(), Guid.NewGuid(), blockedReason);

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion From, SubscriptionPlanVersion To) TwoEditions(
        decimal fromPrice,
        decimal toPrice,
        int fromSeats,
        int toSeats,
        string[]? fromModules = null,
        string[]? toModules = null,
        BillingCycle[]? toCycles = null
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "Pro", "Pro plan", PlanTier.Pro, Admin, Now)
            .Value;
        var from = Edition(
            plan,
            1,
            fromPrice,
            fromSeats,
            fromModules ?? ["meetings"],
            [BillingCycle.Monthly, BillingCycle.Yearly]
        );
        var to = Edition(
            plan,
            2,
            toPrice,
            toSeats,
            toModules ?? ["meetings"],
            toCycles ?? [BillingCycle.Monthly, BillingCycle.Yearly]
        );
        plan.AddVersion(from, Admin, Now);
        plan.PublishVersion(from.Id, Now, Admin, Now);
        plan.AddVersion(to, Admin, Now);
        plan.PublishVersion(to.Id, Now, Admin, Now);
        plan.ClearDomainEvents();
        return (plan, from, to);
    }

    private static SubscriptionPlanVersion Edition(
        SubscriptionPlan plan,
        int number,
        decimal monthly,
        int seats,
        string[] modules,
        BillingCycle[] cycles
    )
    {
        var version = SubscriptionPlanVersion.Create(plan.Id, number, 14, cycles).Value;
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
        foreach (var module in new[] { "customers" }.Concat(modules))
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        foreach (var cycle in cycles)
            version.AddPriceTier(
                PlanPriceTier
                    .Create(
                        version.Id,
                        cycle,
                        1,
                        null,
                        Money.Create(cycle == BillingCycle.Yearly ? monthly * 10 : monthly, "USD").Value
                    )
                    .Value
            );
        return version;
    }

    private static TenantSubscription ActiveOn(
        SubscriptionPlan plan,
        SubscriptionPlanVersion version,
        DateTime periodEnd
    )
    {
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                periodEnd.AddMonths(-1),
                periodEnd,
                Guid.Empty,
                Now
            )
            .Value;
        subscription.ClearDomainEvents();
        return subscription;
    }

    private static Module Commercial(string code)
    {
        var module = Module.Create(code, code, code).Value;
        module.MarkAsCommercial();
        return module;
    }
}
