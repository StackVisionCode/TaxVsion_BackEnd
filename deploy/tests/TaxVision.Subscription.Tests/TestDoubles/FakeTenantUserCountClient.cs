using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Conteo de usuarios de Auth falso. Null simula que Auth no respondió.</summary>
public sealed class FakeTenantUserCountClient(TenantUserCount? count, IReadOnlyDictionary<Guid, int>? perTenant = null)
    : ITenantUserCountClient
{
    public static FakeTenantUserCountClient WithActive(int activeUsers) =>
        new(new TenantUserCount(activeUsers, PendingInvitations: 0));

    public static FakeTenantUserCountClient Unavailable() => new(null);

    /// <summary>Usuarios por oficina; una oficina que no está simula que Auth no respondió.</summary>
    public static FakeTenantUserCountClient PerTenant(IReadOnlyDictionary<Guid, int> activeUsers) =>
        new(null, activeUsers);

    public Task<TenantUserCount?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(
            perTenant is null ? count
            : perTenant.TryGetValue(tenantId, out var users) ? new TenantUserCount(users, PendingInvitations: 0)
            : null
        );
}
