using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Fake de <see cref="ITenantHostResolver"/> para los tests de consumers. Por defecto devuelve
/// <c>null</c> → los consumers caen al base fijo de <c>PortalOptions</c> (el comportamiento previo
/// al resolver M2M), que es justo lo que estos tests asumen. Pasá un host explícito si un test
/// necesita verificar el link per-tenant.
/// </summary>
internal sealed class FakeTenantHostResolver(string? host = null) : ITenantHostResolver
{
    public Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(host);
}
