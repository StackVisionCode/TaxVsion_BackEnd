using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// F2 + R1, solo lectura sobre la base local de desarrollo: para cada oficina, la parte de la foto que sale del plan
/// (fuente Plan) con la regla nueva, aplicada a la edición CONTRATADA (desde R2 la publicada es otra), no pierde ni
/// cambia nada respecto de la guardada. Lo único que puede ganar son claves de plataforma que su edición no tenía
/// (planes creados por la API que el reconciliador no cubre). No escribe nada.
/// </summary>
public sealed class ContractedSnapshotParityIntegrationTests
{
    private static readonly HashSet<string> PlatformKeys = EntitlementKeyCatalog
        .PlatformDefaults.Select(seed => seed.Key)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task Re_anchored_offices_lose_nothing_and_only_gain_missing_platform_keys()
    {
        await using var db = DevelopmentDb();
        var modules = await db.Modules.AsNoTracking().ToListAsync();
        var plans = await db
            .Plans.AsNoTracking()
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Features)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Entitlements)
            .ToDictionaryAsync(plan => plan.Code.Value);
        var subscriptions = await db
            .Subscriptions.IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(subscription => subscription.TenantId);
        var snapshots = await db.EntitlementSnapshots.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        var differences = new List<string>();
        foreach (var snapshot in snapshots)
        {
            if (!subscriptions.TryGetValue(snapshot.TenantId, out var subscription))
                continue;
            var contracted = plans[snapshot.PlanCode]
                .Versions.FirstOrDefault(version => version.Id == subscription.PlanVersionId);
            if (contracted is null)
                continue;

            var expected = ContractedEntitlementPolicy
                .BaseEntries(contracted, modules, SubscriptionAccess.GrantsAccess(subscription.Status))
                .Value;
            var stored = snapshot.Entries.Where(entry => entry.Source == EntitlementSource.Plan).ToList();

            foreach (var entry in stored)
                if (!expected.TryGetValue(entry.Key.Value, out var now) || now.Value != entry.Value)
                    differences.Add(
                        $"{snapshot.TenantId} {entry.Key.Value}: {entry.Value} -> {now?.Value ?? "(none)"}"
                    );

            var storedKeys = snapshot.Entries.Select(entry => entry.Key.Value).ToHashSet(StringComparer.Ordinal);
            foreach (var key in expected.Keys.Where(key => !storedKeys.Contains(key) && !PlatformKeys.Contains(key)))
                differences.Add($"{snapshot.TenantId} {key}: (none) -> {expected[key].Value}");
        }

        Assert.True(snapshots.Count > 0, "La base local no tiene fotos de derechos.");
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    private static SubscriptionDbContext DevelopmentDb()
    {
        var connection =
            new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-subscription1")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set the Subscription Api user-secrets to run this check.");
        return new SubscriptionDbContext(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(connection).Options,
            new NoTenant()
        );
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
