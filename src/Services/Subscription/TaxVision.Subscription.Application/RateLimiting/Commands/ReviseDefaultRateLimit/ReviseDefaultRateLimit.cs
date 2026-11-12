using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Application.RateLimiting.Commands.ReviseDefaultRateLimit;

/// <summary>Cambia el multiplicador del perfil por defecto para una categoría. Bajarlo exige vista previa y motivo.</summary>
public sealed record ReviseDefaultRateLimitCommand(
    string Category,
    decimal Multiplier,
    Guid ActorUserId,
    string? ConfirmationToken,
    string? Reason
);

public static class ReviseDefaultRateLimitHandler
{
    public static async Task<Result> Handle(
        ReviseDefaultRateLimitCommand command,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var preview = await RateLimitChanges.PreviewDefaultAsync(
            command.Category,
            command.Multiplier,
            profiles,
            plans,
            rows,
            subscriptions,
            ct
        );
        if (preview.IsFailure)
            return preview;

        var confirmed = RateLimitChanges.EnsureConfirmed(preview.Value, command.ConfirmationToken, command.Reason);
        if (confirmed.IsFailure)
            return confirmed;

        var profile = await profiles.GetForUpdateAsync(ct);
        if (profile is null)
            return Result.Failure(
                new Error("RateLimit.DefaultProfileMissing", "The default rate limit profile does not exist.")
            );

        var category = RateLimitChanges.ParseCategory(command.Category, scalableOnly: true).Value;
        var before = new { Category = category.ToString(), Multiplier = profile.MultiplierFor(category).Value };
        var nowUtc = DateTime.UtcNow;
        var revised = profile.Revise(
            category,
            RateLimitMultiplier.Create(command.Multiplier).Value,
            command.ActorUserId,
            nowUtc
        );
        if (revised.IsFailure)
            return revised;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "rate_limit.default_revised",
            new CatalogAuditTarget("RateLimitDefaultProfile", profile.Id, category.ToString()),
            before,
            new { Category = category.ToString(), Multiplier = profile.MultiplierFor(category).Value },
            string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
            correlation.CorrelationId,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
