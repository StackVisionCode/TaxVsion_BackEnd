using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Catalog;
using TaxVision.Subscription.Application.Catalog.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Catalog;

public sealed class CommercialModuleShadowReconciler(
    SubscriptionDbContext db,
    IModuleEditSerializer serializer,
    ILogger<CommercialModuleShadowReconciler> logger
) : ICommercialModuleShadowReconciler
{
    public async Task ReconcileAllAsync(CancellationToken ct)
    {
        var moduleCodes = await db.Modules.AsNoTracking().Select(module => module.Code).ToListAsync(ct);
        foreach (var moduleCode in moduleCodes)
            await UpsertShadowForAsync(moduleCode, ct);

        var knownModules = moduleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shadowCodes = (
            await db
                .AddOnDefinitions.AsNoTracking()
                .Where(definition => definition.Category == "module")
                .Select(definition => definition.Code)
                .ToListAsync(ct)
        ).Select(code => code.Value);

        foreach (var shadowCode in shadowCodes)
        {
            const string prefix = "module-addon-";
            if (!shadowCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var moduleCode = shadowCode[prefix.Length..];
            if (!knownModules.Contains(moduleCode))
                await DeprecateShadowForAsync(moduleCode, ct);
        }
    }

    public async Task UpsertShadowForAsync(string moduleCode, CancellationToken ct)
    {
        var normalizedCode = moduleCode.Trim().ToLowerInvariant();
        await using var lease = await serializer.AcquireAsync(normalizedCode, ct);

        var module = await db.Modules.AsNoTracking().FirstOrDefaultAsync(item => item.Code == normalizedCode, ct);
        if (module is null || !module.IsSellableAsAddon)
        {
            await DeprecateShadowCoreAsync(normalizedCode, ct);
            return;
        }

        var shadow = await FindShadowAsync(module.AddOnCode, ct);
        if (shadow is null)
        {
            db.AddOnDefinitions.Add(SubscriptionAddOnCatalogSeeder.BuildModuleShadow(module, DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created add-on shadow for module {ModuleCode}.", normalizedCode);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var changed = false;
        if (shadow.Name != module.Name || shadow.Description != module.Description)
        {
            EnsureSuccess(shadow.UpdateMetadata(module.Name, module.Description, Guid.Empty, nowUtc));
            changed = true;
        }

        if (shadow.Status != AddOnDefinitionStatus.Published)
        {
            EnsureSuccess(shadow.RestoreManagedModuleShadow(Guid.Empty, nowUtc));
            changed = true;
        }

        if (!ShadowFeatureMatches(shadow, module))
        {
            EnsureSuccess(shadow.ReplaceFeatures(BuildShadowFeatures(shadow.Id, module), Guid.Empty, nowUtc));
            changed = true;
        }

        if (!ShadowPricesMatch(shadow, module))
        {
            EnsureSuccess(shadow.ReplacePriceTiers(BuildShadowPriceTiers(shadow.Id, module), Guid.Empty, nowUtc));
            changed = true;
        }

        if (!changed)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Updated add-on shadow for module {ModuleCode}.", normalizedCode);
    }

    public async Task DeprecateShadowForAsync(string moduleCode, CancellationToken ct)
    {
        var normalizedCode = moduleCode.Trim().ToLowerInvariant();
        await using var lease = await serializer.AcquireAsync(normalizedCode, ct);
        await DeprecateShadowCoreAsync(normalizedCode, ct);
    }

    private async Task DeprecateShadowCoreAsync(string moduleCode, CancellationToken ct)
    {
        var shadow = await FindShadowAsync($"module-addon-{moduleCode}", ct);
        if (shadow is null || shadow.Status != AddOnDefinitionStatus.Published)
            return;

        EnsureSuccess(shadow.Deprecate(Guid.Empty, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deprecated add-on shadow for module {ModuleCode}.", moduleCode);
    }

    private Task<AddOnDefinition?> FindShadowAsync(string addOnCode, CancellationToken ct)
    {
        var code = AddOnCode.Create(addOnCode).Value;
        return db
            .AddOnDefinitions.Include(definition => definition.Features)
            .Include(definition => definition.PriceTiers)
            .FirstOrDefaultAsync(definition => definition.Code == code && definition.Category == "module", ct);
    }

    private static bool ShadowFeatureMatches(AddOnDefinition shadow, Module module) =>
        shadow.Features.Count == 1
        && shadow.Features.Single().Enabled
        && shadow.Features.Single().FeatureKey.Value == $"module.{module.Code}";

    private static bool ShadowPricesMatch(AddOnDefinition shadow, Module module) =>
        shadow.PriceTiers.Count == 2
        && shadow.PriceTiers.Any(tier => PriceMatches(tier, BillingCycle.Monthly, module.AddonPriceMonthlyUsd!.Value))
        && shadow.PriceTiers.Any(tier => PriceMatches(tier, BillingCycle.Yearly, module.AddonPriceYearlyUsd!.Value));

    private static bool PriceMatches(AddOnPriceTier tier, BillingCycle cycle, decimal amount) =>
        tier.BillingCycle == cycle
        && tier.MinQuantity == 1
        && tier.MaxQuantity is null
        && tier.UnitAmount.Amount == amount
        && tier.UnitAmount.Currency == "USD";

    private static IReadOnlyCollection<AddOnFeature> BuildShadowFeatures(Guid definitionId, Module module) =>
        [AddOnFeature.Create(definitionId, EntitlementKey.Create($"module.{module.Code}").Value, enabled: true).Value];

    private static IReadOnlyCollection<AddOnPriceTier> BuildShadowPriceTiers(Guid definitionId, Module module) =>
        [
            AddOnPriceTier
                .Create(
                    definitionId,
                    BillingCycle.Monthly,
                    1,
                    null,
                    Money.Create(module.AddonPriceMonthlyUsd!.Value, "USD").Value
                )
                .Value,
            AddOnPriceTier
                .Create(
                    definitionId,
                    BillingCycle.Yearly,
                    1,
                    null,
                    Money.Create(module.AddonPriceYearlyUsd!.Value, "USD").Value
                )
                .Value,
        ];

    private static void EnsureSuccess(BuildingBlocks.Results.Result result)
    {
        if (result.IsFailure)
            throw new InvalidOperationException($"{result.Error.Code}: {result.Error.Message}");
    }
}
