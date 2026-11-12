using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Application.RateLimiting;

/// <summary>
/// Qué pasaría con un cambio de cupo: el valor actual y el propuesto, si es una baja y cuántas oficinas la sentirían.
/// Los rate limits son operativos: un cambio aplica a todas las oficinas de ese plan (o que dependen del perfil).
/// </summary>
public sealed record RateLimitChangePreview(
    string Target,
    string Category,
    decimal CurrentMultiplier,
    decimal ProposedMultiplier,
    int? CurrentHardOverridePerMinute,
    int? ProposedHardOverridePerMinute,
    bool IsDecrease,
    int OfficesAffected,
    string ConfirmationToken
);

public sealed record PreviewDefaultRateLimitChangeQuery(string Category, decimal Multiplier);

public sealed record PreviewPlanRateLimitChangeQuery(
    string PlanCode,
    string Category,
    decimal Multiplier,
    int? HardOverridePerMinute
);

public sealed record GetDefaultRateLimitProfileQuery;

public sealed record DefaultRateLimitResponse(string Category, decimal Multiplier);

/// <summary>Reglas comunes de los cambios de cupo (D12/D13): baja = vista previa, token y motivo.</summary>
public static class RateLimitChanges
{
    public const string DefaultProfileTarget = "default-profile";

    /// <summary>
    /// Una baja: multiplicador menor, o un cupo fijo nuevo, menor o quitado (el cupo fijo reemplaza al
    /// multiplicador y puede quedar por debajo del base).
    /// </summary>
    public static bool IsDecrease(decimal currentMultiplier, int? currentHard, decimal newMultiplier, int? newHard) =>
        newMultiplier < currentMultiplier
        || (newHard is not null && (currentHard is null || newHard < currentHard))
        || (newHard is null && currentHard is not null);

    public static Result<RateLimitCategory> ParseCategory(string category, bool scalableOnly)
    {
        if (
            !Enum.TryParse<RateLimitCategory>(category, ignoreCase: true, out var parsed)
            || (scalableOnly && !RateLimitDefaultProfile.ScalableCategories.Contains(parsed))
        )
            return Result.Failure<RateLimitCategory>(
                new Error("RateLimit.InvalidCategory", $"Unknown or non-scalable rate limit category '{category}'.")
            );

        return Result.Success(parsed);
    }

    public static async Task<Result<RateLimitChangePreview>> PreviewDefaultAsync(
        string categoryText,
        decimal multiplier,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IPlanSubscriptionsAcrossTenants subscriptions,
        CancellationToken ct
    )
    {
        var category = ParseCategory(categoryText, scalableOnly: true);
        if (category.IsFailure)
            return Result.Failure<RateLimitChangePreview>(category.Error);
        var proposed = RateLimitMultiplier.Create(multiplier);
        if (proposed.IsFailure)
            return Result.Failure<RateLimitChangePreview>(proposed.Error);

        var profile = await profiles.GetAsync(ct);
        if (profile is null)
            return Result.Failure<RateLimitChangePreview>(ProfileMissing);

        // Afecta a las oficinas de los planes que no tienen fila propia para la categoría.
        var withRow = (await rows.GetAllAsync(ct))
            .Where(row => row.Category == category.Value)
            .Select(row => row.PlanCode.Value)
            .ToHashSet(StringComparer.Ordinal);
        var offices = 0;
        foreach (var plan in await plans.GetAllAsync(ct))
            if (!withRow.Contains(plan.Code.Value))
                offices += (await subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct)).Count;

        return Result.Success(
            Build(
                DefaultProfileTarget,
                category.Value,
                profile.MultiplierFor(category.Value).Value,
                currentHard: null,
                proposed.Value.Value,
                proposedHard: null,
                offices
            )
        );
    }

    public static async Task<Result<RateLimitChangePreview>> PreviewPlanAsync(
        string planCode,
        string categoryText,
        decimal multiplier,
        int? hardOverridePerMinute,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IPlanSubscriptionsAcrossTenants subscriptions,
        CancellationToken ct
    )
    {
        var category = ParseCategory(categoryText, scalableOnly: false);
        if (category.IsFailure)
            return Result.Failure<RateLimitChangePreview>(category.Error);
        var proposed = RateLimitMultiplier.Create(multiplier);
        if (proposed.IsFailure)
            return Result.Failure<RateLimitChangePreview>(proposed.Error);
        if (hardOverridePerMinute is <= 0)
            return Result.Failure<RateLimitChangePreview>(
                new Error("PlanRateLimit.InvalidHardOverride", "Hard override, when set, must be greater than zero.")
            );

        // Sin fila, hoy rige el perfil por defecto.
        var row = await rows.GetForUpdateAsync(planCode, category.Value, ct);
        var profile = await profiles.GetAsync(ct);
        var currentMultiplier =
            row?.MultiplierOverride ?? profile?.MultiplierFor(category.Value).Value ?? RateLimitMultiplier.Base;

        var plan = await plans.GetByCodeAsync(planCode, ct);
        var offices = plan is null ? 0 : (await subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct)).Count;

        return Result.Success(
            Build(
                planCode,
                category.Value,
                currentMultiplier,
                row?.HardOverridePerMinute,
                proposed.Value.Value,
                hardOverridePerMinute,
                offices
            )
        );
    }

    /// <summary>Si es una baja: motivo, token y que el token sea el de la vista previa del estado actual.</summary>
    public static Result EnsureConfirmed(RateLimitChangePreview current, string? confirmationToken, string? reason)
    {
        if (!current.IsDecrease)
            return Result.Success();

        var request = CatalogConfirmation.ValidateRequest(confirmationToken, reason);
        if (request.IsFailure)
            return request;

        return CatalogConfirmation.Matches(current.ConfirmationToken, confirmationToken!)
            ? Result.Success()
            : Result.Failure(CatalogConfirmation.Stale);
    }

    private static readonly Error ProfileMissing = new(
        "RateLimit.DefaultProfileMissing",
        "The default rate limit profile does not exist."
    );

    private static RateLimitChangePreview Build(
        string target,
        RateLimitCategory category,
        decimal currentMultiplier,
        int? currentHard,
        decimal proposedMultiplier,
        int? proposedHard,
        int offices
    )
    {
        var isDecrease = IsDecrease(currentMultiplier, currentHard, proposedMultiplier, proposedHard);
        var token = CatalogConfirmation.Token(
            "rate-limit",
            target,
            category,
            currentMultiplier,
            currentHard,
            proposedMultiplier,
            proposedHard,
            offices
        );
        return new RateLimitChangePreview(
            target,
            category.ToString(),
            currentMultiplier,
            proposedMultiplier,
            currentHard,
            proposedHard,
            isDecrease,
            offices,
            token
        );
    }
}

public static class PreviewDefaultRateLimitChangeHandler
{
    public static Task<Result<RateLimitChangePreview>> Handle(
        PreviewDefaultRateLimitChangeQuery query,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IPlanSubscriptionsAcrossTenants subscriptions,
        CancellationToken ct
    ) =>
        RateLimitChanges.PreviewDefaultAsync(
            query.Category,
            query.Multiplier,
            profiles,
            plans,
            rows,
            subscriptions,
            ct
        );
}

public static class PreviewPlanRateLimitChangeHandler
{
    public static Task<Result<RateLimitChangePreview>> Handle(
        PreviewPlanRateLimitChangeQuery query,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IPlanSubscriptionsAcrossTenants subscriptions,
        CancellationToken ct
    ) =>
        RateLimitChanges.PreviewPlanAsync(
            query.PlanCode,
            query.Category,
            query.Multiplier,
            query.HardOverridePerMinute,
            profiles,
            plans,
            rows,
            subscriptions,
            ct
        );
}

public static class GetDefaultRateLimitProfileHandler
{
    public static async Task<Result<IReadOnlyList<DefaultRateLimitResponse>>> Handle(
        GetDefaultRateLimitProfileQuery query,
        IRateLimitDefaultProfileRepository profiles,
        CancellationToken ct
    )
    {
        var profile = await profiles.GetAsync(ct);
        if (profile is null)
            return Result.Failure<IReadOnlyList<DefaultRateLimitResponse>>(
                new Error("RateLimit.DefaultProfileMissing", "The default rate limit profile does not exist.")
            );

        return Result.Success<IReadOnlyList<DefaultRateLimitResponse>>(
            RateLimitDefaultProfile
                .ScalableCategories.Select(category => new DefaultRateLimitResponse(
                    category.ToString(),
                    profile.MultiplierFor(category).Value
                ))
                .ToList()
        );
    }
}
