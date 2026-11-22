using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Repositorio de dispositivos en memoria, entre oficinas, con la misma semántica que el de EF.</summary>
internal sealed class InMemoryPushDevices : IPushDeviceTokenRepository
{
    public List<PushDeviceToken> All { get; } = [];

    public Task AddAsync(PushDeviceToken token, CancellationToken ct = default)
    {
        All.Add(token);
        return Task.CompletedTask;
    }

    public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
        Task.FromResult(All.FirstOrDefault(d => d.TenantId == tenantId && d.Token == token));

    public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(All.FirstOrDefault(d => d.TenantId == tenantId && d.Id == id));

    public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PushDeviceToken>>(
            All.Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive).ToList()
        );

    public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        All.FirstOrDefault(d => d.TenantId == tenantId && d.Id == id)?.Revoke();
        return Task.CompletedTask;
    }

    public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Revoke(d => d.TenantId == tenantId && d.UserId == userId);

    public Task<int> RevokeForSessionAsync(
        Guid tenantId,
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default
    ) => Revoke(d => d.TenantId == tenantId && d.UserId == userId && d.SessionId == sessionId);

    public Task<int> RevokeAllActiveForUserExceptSessionAsync(
        Guid tenantId,
        Guid userId,
        Guid? exceptSessionId,
        CancellationToken ct = default
    ) =>
        Revoke(d =>
            d.TenantId == tenantId && d.UserId == userId && (exceptSessionId is null || d.SessionId != exceptSessionId)
        );

    public Task<int> DeactivateTokenInOtherTenantsAsync(
        string token,
        Guid exceptTenantId,
        CancellationToken ct = default
    ) => Revoke(d => d.Token == token && d.TenantId != exceptTenantId);

    private Task<int> Revoke(Func<PushDeviceToken, bool> match)
    {
        var targets = All.Where(d => d.IsActive && match(d)).ToList();
        foreach (var device in targets)
            device.Revoke();
        return Task.FromResult(targets.Count);
    }
}
