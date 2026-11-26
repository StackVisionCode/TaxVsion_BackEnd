using BuildingBlocks.RateLimiting;

namespace TaxVision.Connectors.Tests.Sync;

/// <summary>Módulos de la oficina en pruebas. Sin lista (null) equivale a no tener proyección todavía.</summary>
internal sealed class FakeTenantEntitlementModulesReader(IReadOnlyList<string>? modules)
    : ITenantEntitlementModulesReader
{
    public static FakeTenantEntitlementModulesReader Unrestricted => new(null);

    public static FakeTenantEntitlementModulesReader WithoutEmail => new(["customers"]);

    public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(modules);
}
