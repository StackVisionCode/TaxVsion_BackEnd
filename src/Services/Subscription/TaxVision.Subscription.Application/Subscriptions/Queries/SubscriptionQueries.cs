using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public sealed record PlanResponse(
    Guid Id,
    string Code,
    string Name,
    string Description,
    decimal MonthlyPriceUsd,
    int MaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules);

/// <summary>Catálogo público de planes para la landing.</summary>
public sealed record GetPlansQuery;

public static class GetPlansHandler
{
    public static async Task<Result<IReadOnlyList<PlanResponse>>> Handle(
        GetPlansQuery query,
        IPlanRepository plans,
        CancellationToken ct)
    {
        var active = await plans.GetActiveAsync(ct);
        IReadOnlyList<PlanResponse> response = active
            .Select(plan => new PlanResponse(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.Description,
                plan.MonthlyPriceUsd,
                plan.MaxUsers,
                plan.MaxPendingInvitations,
                plan.StorageQuotaBytes,
                ParseModules(plan.EnabledModulesJson)))
            .ToList();
        return Result.Success(response);
    }

    internal static IReadOnlyList<string> ParseModules(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed record MySubscriptionResponse(
    string PlanCode,
    string PlanName,
    string Status,
    decimal MonthlyPriceUsd,
    int IncludedUsers,
    int ExtraSeats,
    int EffectiveMaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules,
    DateTime? TrialEndsAtUtc,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? CancelledAtUtc);

public sealed record GetMySubscriptionQuery(Guid TenantId);

public static class GetMySubscriptionHandler
{
    public static async Task<Result<MySubscriptionResponse>> Handle(
        GetMySubscriptionQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
        {
            return Result.Failure<MySubscriptionResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist."));
        }

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
        {
            return Result.Failure<MySubscriptionResponse>(
                new Error("Plan.NotFound", "Plan does not exist."));
        }

        return Result.Success(new MySubscriptionResponse(
            plan.Code,
            plan.Name,
            subscription.Status.ToString(),
            plan.MonthlyPriceUsd,
            plan.MaxUsers,
            subscription.ExtraSeats,
            subscription.EffectiveMaxUsers(plan),
            plan.MaxPendingInvitations,
            plan.StorageQuotaBytes,
            GetPlansHandler.ParseModules(plan.EnabledModulesJson),
            subscription.TrialEndsAtUtc,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            subscription.CancelledAtUtc));
    }
}
