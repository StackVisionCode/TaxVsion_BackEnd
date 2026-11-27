using BuildingBlocks.RateLimiting;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Módulos de la oficina en memoria. Sin valor = sin proyección todavía (null).</summary>
internal sealed class FakeTenantEntitlementModulesReader : ITenantEntitlementModulesReader
{
    public IReadOnlyList<string>? Modules { get; set; }

    public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Modules);
}
