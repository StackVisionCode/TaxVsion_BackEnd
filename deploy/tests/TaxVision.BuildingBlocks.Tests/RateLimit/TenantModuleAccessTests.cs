using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// "¿La oficina tiene el módulo?" para los procesos automáticos. Sin proyección todavía, sí: no se corta a
/// una oficina por consistencia eventual.
/// </summary>
public sealed class TenantModuleAccessTests
{
    [Fact]
    public async Task An_office_with_the_module_has_access()
    {
        var reader = new Reader(["signature", "email"]);

        Assert.True(await reader.HasModuleAsync(Guid.NewGuid(), "Signature"));
    }

    [Fact]
    public async Task An_office_without_modules_has_no_access()
    {
        var reader = new Reader([]);

        Assert.False(await reader.HasModuleAsync(Guid.NewGuid(), "signature"));
    }

    [Fact]
    public async Task An_office_without_projection_yet_keeps_access()
    {
        var reader = new Reader(null);

        Assert.True(await reader.HasModuleAsync(Guid.NewGuid(), "signature"));
    }

    private sealed class Reader(IReadOnlyList<string>? modules) : ITenantEntitlementModulesReader
    {
        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(modules);
    }
}
