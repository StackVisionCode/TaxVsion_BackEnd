using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Catalog.Migrations;

public sealed record SubscriberMigrationOfficePreview(
    Guid TenantId,
    Guid SubscriptionId,
    Guid FromVersionId,
    int FromEdition,
    string Cycle,
    long? PriceFromCents,
    long? PriceToCents,
    int? SeatsFrom,
    int? SeatsTo,
    long? StorageFromBytes,
    long? StorageToBytes,
    IReadOnlyList<string> ModulesLost,
    IReadOnlyList<string> ModulesGained,
    bool OnlyImprovement,
    string Summary,
    DateTime? EffectiveAtUtc,
    string? BlockedReason
);

public sealed record SubscriberMigrationPreview(
    Guid PlanId,
    string PlanCode,
    Guid ToVersionId,
    int ToEdition,
    string Mode,
    int NoticeDays,
    bool OnlyImprovement,
    IReadOnlyList<SubscriberMigrationOfficePreview> Offices,
    string PreviewToken
);

/// <summary>
/// D11: arma la vista previa de mover las oficinas de un plan a otra edición: qué cambia para cada una, cuándo, y
/// quién queda bloqueada (más usuarios o datos que lo que da la edición nueva, o sin poder medirlo). Solo pregunta a
/// Auth y a CloudStorage cuando la edición nueva recorta asientos o almacenamiento. No cambia nada.
/// </summary>
public sealed class SubscriberMigrationPlanner(
    IPlanRepository plans,
    IModuleRepository modules,
    IPlanSubscriptionsAcrossTenants subscriptions,
    ISubscriberMigrationReads reads,
    ITenantUserCountClient users,
    IStorageUsageClient storage
)
{
    public async Task<Result<SubscriberMigrationPreview>> PreviewAsync(
        Guid planId,
        Guid toVersionId,
        string modeText,
        int noticeDays,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<SubscriberMigrationMode>(modeText, ignoreCase: true, out var mode))
            return Fail("Migration.InvalidMode", "Mode must be Immediate or AtNextRenewal.");

        var plan = await plans.GetByIdAsync(planId, ct);
        if (plan is null)
            return Fail("Plan.NotFound", "Plan not found.");

        var to = plan.Versions.FirstOrDefault(version => version.Id == toVersionId);
        if (to is null || to.Status == PlanVersionStatus.Draft)
            return Fail("Migration.VersionNotFound", "The target edition does not exist or is still a draft.");

        var catalog = await modules.GetAllAsync(ct);
        var offices = (await subscriptions.ListByPlanAcrossTenantsAsync(planId, ct))
            .Where(subscription =>
                subscription.PlanVersionId != to.Id
                && subscription.Status is not (SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
            )
            .OrderBy(subscription => subscription.TenantId)
            .ToList();
        var holdings = await reads.GetHoldingsAsync(offices.Select(office => office.TenantId).ToList(), ct);

        var previews = new List<SubscriberMigrationOfficePreview>();
        foreach (var office in offices)
        {
            var from = plan.Versions.FirstOrDefault(version => version.Id == office.PlanVersionId);
            if (from is null)
            {
                previews.Add(Unknown(office, "Subscription.ContractedVersionNotFound"));
                continue;
            }

            var comparison = VersionChangeComparison.Compare(from, to, office.BillingCycle, catalog);
            var blocked = await BlockReasonAsync(office, comparison, holdings[office.TenantId], ct);
            previews.Add(
                new SubscriberMigrationOfficePreview(
                    office.TenantId,
                    office.Id,
                    from.Id,
                    from.VersionNumber,
                    office.BillingCycle.ToString(),
                    comparison.PriceFromCents,
                    comparison.PriceToCents,
                    comparison.SeatsFrom,
                    comparison.SeatsTo,
                    comparison.StorageFromBytes,
                    comparison.StorageToBytes,
                    comparison.ModulesLost,
                    comparison.ModulesGained,
                    comparison.IsOnlyImprovement,
                    comparison.Summary(),
                    mode == SubscriberMigrationMode.AtNextRenewal
                        ? RenewalSchedule.FirstRenewalOnOrAfter(office, nowUtc.AddDays(noticeDays))
                        : null,
                    blocked
                )
            );
        }

        var movable = previews.Where(office => office.BlockedReason is null).ToList();
        var token = CatalogConfirmation.Token(
            "subscriber-migration",
            planId,
            to.Id,
            mode,
            mode == SubscriberMigrationMode.Immediate ? 0 : noticeDays,
            string.Join(',', previews.Select(o => $"{o.TenantId}:{o.FromVersionId}:{o.BlockedReason}"))
        );

        return Result.Success(
            new SubscriberMigrationPreview(
                plan.Id,
                plan.Code.Value,
                to.Id,
                to.VersionNumber,
                mode.ToString(),
                mode == SubscriberMigrationMode.Immediate ? 0 : noticeDays,
                movable.All(office => office.OnlyImprovement),
                previews,
                token
            )
        );
    }

    /// <summary>Si la oficina no entra en la edición nueva hoy (la usa también el job antes de agendar).</summary>
    public async Task<string?> CapacityBlockAsync(
        TenantSubscription office,
        SubscriptionPlan plan,
        Guid toVersionId,
        IReadOnlyCollection<Module> catalog,
        CancellationToken ct
    )
    {
        var from = plan.Versions.FirstOrDefault(version => version.Id == office.PlanVersionId);
        var to = plan.Versions.FirstOrDefault(version => version.Id == toVersionId);
        if (from is null || to is null)
            return "Migration.VersionNotFound";

        var holdings = await reads.GetHoldingsAsync([office.TenantId], ct);
        var comparison = VersionChangeComparison.Compare(from, to, office.BillingCycle, catalog);
        return await BlockReasonAsync(office, comparison, holdings[office.TenantId], ct);
    }

    private async Task<string?> BlockReasonAsync(
        TenantSubscription office,
        VersionChangeComparison comparison,
        OfficeHoldings holdings,
        CancellationToken ct
    )
    {
        if (!comparison.CycleAvailable)
            return "Plan.CycleNotAvailable";
        if (office.HasPlanChangeInProgress)
            return "Subscription.HasPendingPlanChange";
        if (office.ScheduledVersionId is not null)
            return "Subscription.VersionChangeAlreadyScheduled";

        // Asientos: los del plan más los comprados, contra la gente que hoy ocupa cupo.
        if (comparison.SeatsTo < comparison.SeatsFrom)
        {
            var occupancy = await users.GetAsync(office.TenantId, ct);
            if (occupancy is null)
                return "Migration.UsageUnavailable";
            if (occupancy.Occupied > (comparison.SeatsTo ?? 0) + holdings.LiveSeats)
                return "Migration.ExceedsSeats";
        }

        // Almacenamiento: la cuota del plan más lo que suman sus add-ons, contra lo que ya usa.
        if (comparison.StorageToBytes < comparison.StorageFromBytes)
        {
            var used = await storage.GetUsedBytesAsync(office.TenantId, ct);
            if (used is null)
                return "Migration.UsageUnavailable";
            var addOns = Math.Max(0, (holdings.SnapshotStorageMaxBytes ?? 0) - (comparison.StorageFromBytes ?? 0));
            if (used > (comparison.StorageToBytes ?? 0) + addOns)
                return "Migration.ExceedsStorage";
        }

        return null;
    }

    private static SubscriberMigrationOfficePreview Unknown(TenantSubscription office, string reason) =>
        new(
            office.TenantId,
            office.Id,
            office.PlanVersionId,
            0,
            office.BillingCycle.ToString(),
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            false,
            string.Empty,
            null,
            reason
        );

    private static Result<SubscriberMigrationPreview> Fail(string code, string message) =>
        Result.Failure<SubscriberMigrationPreview>(new Error(code, message));
}
