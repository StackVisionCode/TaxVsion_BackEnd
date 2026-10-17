using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;

public static class UpsertPlanRateLimitHandler
{
    public static async Task<Result> Handle(
        UpsertPlanRateLimitCommand command,
        IPlanRateLimitRepository rateLimits,
        IUnitOfWork unitOfWork,
        ILogger<PlanRateLimit> logger,
        CancellationToken ct
    )
    {
        var codeResult = PlanCode.Create(command.PlanCode);
        if (codeResult.IsFailure)
            return Result.Failure(codeResult.Error);

        if (!Enum.TryParse<RateLimitCategory>(command.Category, ignoreCase: true, out var category))
            return Result.Failure(
                new Error("RateLimit.InvalidCategory", $"Unknown rate limit category '{command.Category}'.")
            );

        var existing = await rateLimits.GetForUpdateAsync(command.PlanCode, category, ct);
        if (existing is not null)
        {
            var revised = existing.Revise(command.MultiplierOverride, command.HardOverridePerMinute);
            if (revised.IsFailure)
                return revised;
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
