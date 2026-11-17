using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Discounts;

/// <summary>El PlatformAdmin da un descuento a una oficina (retención o acuerdo), sin código. Exige motivo.</summary>
public sealed record GrantSubscriptionDiscountCommand(
    Guid TenantId,
    int? PercentBasisPoints,
    long? AmountCents,
    string? Currency,
    DiscountDuration Duration,
    int? DurationMonths,
    DiscountAppliesTo AppliesTo,
    string Reason,
    bool Replace,
    Guid ActorUserId
);

/// <summary>El PlatformAdmin termina el descuento vigente de una oficina.</summary>
public sealed record EndSubscriptionDiscountCommand(Guid TenantId, string Reason, Guid ActorUserId);

/// <summary>El descuento vigente de una oficina; null si no tiene.</summary>
public sealed record GetSubscriptionDiscountQuery(Guid TenantId);

public sealed record SubscriptionDiscountView(
    DiscountSource Source,
    int? PercentBasisPoints,
    long? AmountCents,
    string? Currency,
    DiscountDuration Duration,
    int? Months,
    DiscountAppliesTo AppliesTo,
    string? PlanCode,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    string? Reason
)
{
    public static SubscriptionDiscountView From(SubscriptionDiscount discount) =>
        new(
            discount.Source,
            discount.PercentBasisPoints,
            discount.AmountCents,
            discount.Currency,
            discount.Duration,
            discount.Months,
            discount.AppliesTo,
            discount.PlanCode,
            discount.StartsAtUtc,
            discount.EndsAtUtc,
            discount.Reason
        );
}

public static class GrantSubscriptionDiscountHandler
{
    public static async Task<Result<SubscriptionDiscountView>> Handle(
        GrantSubscriptionDiscountCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ISubscriptionAuditLogWriter audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetWithFullHistoryAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<SubscriptionDiscountView>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var nowUtc = DateTime.UtcNow;
        // "Once" cubre el próximo cobro de la base: la renovación que viene.
        var startsAtUtc =
            command.Duration == DiscountDuration.Once
                ? subscription.NextRenewalAtUtc ?? subscription.CurrentPeriodEndUtc
                : nowUtc;
        var terms = SubscriptionDiscountTerms.Create(
            DiscountSource.Admin,
            codeDefinitionId: null,
            command.PercentBasisPoints,
            command.AmountCents,
            command.Currency,
            command.Duration,
            command.DurationMonths,
            command.AppliesTo,
            planCode: null,
            command.Reason,
            startsAtUtc
        );
        if (terms.IsFailure)
            return Result.Failure<SubscriptionDiscountView>(terms.Error);

        var granted = subscription.GrantDiscount(terms.Value, command.Replace, command.ActorUserId, nowUtc);
        if (granted.IsFailure)
            return Result.Failure<SubscriptionDiscountView>(granted.Error);

        var view = SubscriptionDiscountView.From(granted.Value);
        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.DiscountGranted",
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: view,
            terms.Value.Reason,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(view);
    }
}

public static class EndSubscriptionDiscountHandler
{
    public static async Task<Result> Handle(
        EndSubscriptionDiscountCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ISubscriptionAuditLogWriter audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetWithFullHistoryAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var nowUtc = DateTime.UtcNow;
        var before = subscription.ActiveDiscount(nowUtc) is { } current ? SubscriptionDiscountView.From(current) : null;
        var ended = subscription.EndDiscount(command.Reason, command.ActorUserId, nowUtc);
        if (ended.IsFailure)
            return ended;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.DiscountEnded",
            command.ActorUserId,
            correlation.CorrelationId,
            before,
            after: (object?)null,
            command.Reason.Trim(),
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class GetSubscriptionDiscountHandler
{
    public static async Task<Result<SubscriptionDiscountView?>> Handle(
        GetSubscriptionDiscountQuery query,
        ISubscriptionRepository subscriptions,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetWithFullHistoryAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<SubscriptionDiscountView?>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        return Result.Success(
            subscription.ActiveDiscount(DateTime.UtcNow) is { } discount
                ? SubscriptionDiscountView.From(discount)
                : null
        );
    }
}
