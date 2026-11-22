using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Retention;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>Red de seguridad de los tokens de push: los dormidos se apagan y los revocados viejos se borran.</summary>
public sealed class PushDeviceRetentionTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private readonly NotificationTempDatabase _database = new("notpush_");
    private readonly Guid _tenant = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Idle_devices_are_deactivated()
    {
        var idle = Device("idle", lastSeenUtc: Now.AddDays(-31));
        var recent = Device("recent", lastSeenUtc: Now.AddDays(-5));
        await SeedAsync(idle, recent);

        int deactivated;
        await using (var db = _database.NewContext())
            deactivated = await new PushDeviceTokenRetentionPurger(db).DeactivateIdleAsync(
                Now.AddDays(-30),
                Now,
                batchSize: 1,
                CancellationToken.None
            );

        Assert.Equal(1, deactivated);
        var rows = await LoadAsync();
        Assert.False(rows[idle.Id].IsActive);
        Assert.Equal(Now, rows[idle.Id].RevokedAtUtc);
        Assert.True(rows[recent.Id].IsActive);
    }

    [Fact]
    public async Task Old_revoked_devices_are_deleted()
    {
        var oldRevoked = Device("old-revoked", lastSeenUtc: Now.AddDays(-200), revokedAtUtc: Now.AddDays(-91));
        var newRevoked = Device("new-revoked", lastSeenUtc: Now.AddDays(-20), revokedAtUtc: Now.AddDays(-10));
        var active = Device("active", lastSeenUtc: Now.AddDays(-1));
        await SeedAsync(oldRevoked, newRevoked, active);

        int deleted;
        await using (var db = _database.NewContext())
            deleted = await new PushDeviceTokenRetentionPurger(db).DeleteRevokedAsync(
                Now.AddDays(-90),
                batchSize: 1,
                CancellationToken.None
            );

        Assert.Equal(1, deleted);
        var rows = await LoadAsync();
        Assert.False(rows.ContainsKey(oldRevoked.Id));
        Assert.True(rows.ContainsKey(newRevoked.Id));
        Assert.True(rows.ContainsKey(active.Id));
    }

    private PushDeviceToken Device(string token, DateTime lastSeenUtc, DateTime? revokedAtUtc = null)
    {
        var device = PushDeviceToken
            .Register(_tenant, Guid.NewGuid(), PushPlatform.Fcm, token, deviceId: null, nowUtc: lastSeenUtc)
            .Value;
        if (revokedAtUtc is { } revoked)
        {
            device.Revoke();
            typeof(PushDeviceToken).GetProperty(nameof(PushDeviceToken.RevokedAtUtc))!.SetValue(device, revoked);
        }
        return device;
    }

    private async Task SeedAsync(params PushDeviceToken[] devices)
    {
        await using var seed = _database.NewContext();
        seed.PushDeviceTokens.AddRange(devices);
        await seed.SaveChangesAsync();
    }

    private async Task<Dictionary<Guid, PushDeviceToken>> LoadAsync()
    {
        await using var verify = _database.NewContext();
        return await verify.PushDeviceTokens.IgnoreQueryFilters().ToDictionaryAsync(device => device.Id);
    }
}
