using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;

/// <summary>
/// Crea o cambia la fila de un plan para una categoría. Nunca por debajo del cupo base; bajar el cupo (multiplicador
/// menor o cupo fijo nuevo, menor o quitado) exige la vista previa y un motivo. Todo queda auditado.
/// </summary>
public static class UpsertPlanRateLimitHandler
{
    public static async Task<Result> Handle(
        UpsertPlanRateLimitCommand command,
        IPlanRateLimitRepository rateLimits,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        ILogger<PlanRateLimit> logger,
        CancellationToken ct
    )
    {
        var codeResult = PlanCode.Create(command.PlanCode);
        if (codeResult.IsFailure)
            return Result.Failure(codeResult.Error);

        var preview = await RateLimitChanges.PreviewPlanAsync(
            command.PlanCode,
            command.Category,
            command.MultiplierOverride,
            command.HardOverridePerMinute,
            profiles,
            plans,
            rateLimits,
            subscriptions,
            ct
        );
        if (preview.IsFailure)
            return preview;

        var confirmed = RateLimitChanges.EnsureConfirmed(preview.Value, command.ConfirmationToken, command.Reason);
        if (confirmed.IsFailure)
            return confirmed;

        var category = RateLimitChanges.ParseCategory(command.Category, scalableOnly: false).Value;
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        var target = $"{command.PlanCode}/{category}";
        var existing = await rateLimits.GetForUpdateAsync(command.PlanCode, category, ct);
        if (existing is not null)
        {
            var before = CatalogAuditViews.Of(existing);
            var revised = existing.Revise(command.MultiplierOverride, command.HardOverridePerMinute);
            if (revised.IsFailure)
                return revised;

            await CatalogAudit.RecordIfChangedAsync(
                audit,
                CatalogAuditActor.ForUser(command.ActorUserId),
                "rate_limit.revised",
                new CatalogAuditTarget("PlanRateLimit", existing.Id, target),
                before,
                CatalogAuditViews.Of(existing),
                reason,
                correlation.CorrelationId,
                DateTime.UtcNow,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "PlanRateLimit {Plan}/{Category} revised to multiplier={Multiplier}, hardOverride={HardOverride} by {ActorUserId}.",
                command.PlanCode,
                category,
                command.MultiplierOverride,
                command.HardOverridePerMinute,
                command.ActorUserId
            );
            return Result.Success();
        }

        var seedResult = PlanRateLimit.Seed(
            Guid.NewGuid(),
            codeResult.Value,
            category,
            command.MultiplierOverride,
            command.HardOverridePerMinute
        );
        if (seedResult.IsFailure)
            return seedResult;

        await rateLimits.AddAsync(seedResult.Value, ct);
        await CatalogAudit.RecordAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "rate_limit.created",
            new CatalogAuditTarget("PlanRateLimit", seedResult.Value.Id, target),
            new
            {
                Source = "default-profile",
                preview.Value.CurrentMultiplier,
                preview.Value.CurrentHardOverridePerMinute,
            },
            CatalogAuditViews.Of(seedResult.Value),
            reason,
            correlation.CorrelationId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "PlanRateLimit {Plan}/{Category} created with multiplier={Multiplier}, hardOverride={HardOverride} by {ActorUserId}.",
            command.PlanCode,
            category,
            command.MultiplierOverride,
            command.HardOverridePerMinute,
            command.ActorUserId
        );
        return Result.Success();
    }
}
