using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Conteo de usuarios de Auth falso. Null simula que Auth no respondió.</summary>
public sealed class FakeTenantUserCountClient(TenantUserCount? count) : ITenantUserCountClient
{
    public static FakeTenantUserCountClient WithActive(int activeUsers) =>
        new(new TenantUserCount(activeUsers, PendingInvitations: 0));

    public static FakeTenantUserCountClient Unavailable() => new(null);

    public Task<TenantUserCount?> GetAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(count);
}
