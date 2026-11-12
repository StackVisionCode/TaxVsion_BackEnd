using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Catalog.Migrations;

/// <summary>
/// Lo que el job hace con UNA oficina, sin E/S: la mueve ya, le agenda el cambio para su renovación, la bloquea o la
/// omite, y lo anota en la migración. La medición de capacidad (HTTP) llega ya resuelta, fuera de la transacción.
/// </summary>
public static class SubscriberMigrationSteps
{
    public static void ApplyToOffice(
        SubscriberMigration migration,
        TenantSubscription office,
        SubscriptionPlan plan,
        IReadOnlyCollection<Module> catalog,
        string? capacityBlock,
        DateTime nowUtc
    )
    {
        if (!migration.PendingTenantIds().Contains(office.TenantId))
            return;

        if (office.PlanId != migration.PlanId)
        {
            migration.MarkSkipped(office.TenantId, "Migration.PlanChanged", nowUtc);
            return;
        }

        if (office.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            migration.MarkSkipped(office.TenantId, "Migration.OfficeInactive", nowUtc);
            return;
        }

        if (office.PlanVersionId == migration.ToVersionId)
        {
            migration.MarkApplied(office.TenantId, nowUtc);
            return;
        }

        if (capacityBlock is not null)
        {
            migration.MarkBlocked(office.TenantId, capacityBlock, nowUtc);
            return;
        }

        var from = plan.Versions.FirstOrDefault(version => version.Id == office.PlanVersionId);
        var to = plan.Versions.FirstOrDefault(version => version.Id == migration.ToVersionId);
        if (from is null || to is null)
        {
            migration.MarkBlocked(office.TenantId, "Migration.VersionNotFound", nowUtc);
            return;
        }

        var comparison = VersionChangeComparison.Compare(from, to, office.BillingCycle, catalog);
        if (!comparison.CycleAvailable)
        {
            migration.MarkBlocked(office.TenantId, "Plan.CycleNotAvailable", nowUtc);
            return;
        }

        if (migration.Mode == SubscriberMigrationMode.Immediate)
        {
            if (!comparison.IsOnlyImprovement)
            {
                migration.MarkBlocked(office.TenantId, "Migration.NotImprovement", nowUtc);
                return;
            }

            var applied = office.ApplyVersionChangeNow(plan, to.Id, migration.Id, migration.RequestedBy, nowUtc);
            if (applied.IsFailure)
                migration.MarkBlocked(office.TenantId, applied.Error.Code, nowUtc);
            else
                migration.MarkApplied(office.TenantId, nowUtc);
            return;
        }

        // El aviso sale hoy: la fecha cuenta los días de aviso desde ahora.
        var effectiveAtUtc = RenewalSchedule.FirstRenewalOnOrAfter(office, nowUtc.AddDays(migration.NoticeDays));
        var scheduled = office.ScheduleVersionChange(
            plan,
            to.Id,
            effectiveAtUtc,
            migration.Id,
            comparison.Summary(),
            migration.RequestedBy,
            nowUtc
        );
        if (scheduled.IsFailure)
            migration.MarkBlocked(office.TenantId, scheduled.Error.Code, nowUtc);
        else
            migration.MarkScheduled(office.TenantId, effectiveAtUtc, nowUtc);
    }

    /// <summary>Migración cancelada: deshace el cambio agendado de la oficina si todavía no se aplicó.</summary>
    public static void UnscheduleOffice(SubscriberMigration migration, TenantSubscription office, DateTime nowUtc)
    {
        if (!migration.ScheduledTenantIds().Contains(office.TenantId))
            return;

        office.CancelScheduledVersionChange(migration.Id, migration.RequestedBy, nowUtc);
        migration.MarkUnscheduled(office.TenantId, nowUtc);
    }
}
