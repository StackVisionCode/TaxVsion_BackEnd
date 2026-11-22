using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Push.Commands;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Alta del dispositivo: guarda su sesión y deja un token activo en una sola oficina.</summary>
public sealed class RegisterPushDeviceTokenTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Stores_the_session_and_refreshes_last_seen_at_most_twice_a_day()
    {
        var devices = new InMemoryPushDevices();
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero));
        var tenantId = Guid.NewGuid();
        var s1 = Guid.NewGuid();

        await Register(devices, clock, tenantId, s1);
        var device = Assert.Single(devices.All);
        Assert.Equal(s1, device.SessionId);
        Assert.Equal(clock.UtcNow, device.LastSeenAtUtc);

        var firstSeen = device.LastSeenAtUtc;
        clock.Advance(TimeSpan.FromHours(1));
        await Register(devices, clock, tenantId, s1);
        Assert.Equal(firstSeen, device.LastSeenAtUtc);

        var s2 = Guid.NewGuid();
        clock.Advance(TimeSpan.FromHours(12));
        await Register(devices, clock, tenantId, s2);
        Assert.Equal(s2, device.SessionId);
        Assert.Equal(clock.UtcNow, device.LastSeenAtUtc);
    }

    [Fact]
    public async Task Registering_in_office_B_deactivates_the_same_token_in_office_A()
    {
        var devices = new InMemoryPushDevices();
        var clock = new MovableClock(DateTimeOffset.UtcNow);
        var officeA = Guid.NewGuid();
        var officeB = Guid.NewGuid();

        await Register(devices, clock, officeA, Guid.NewGuid());
        await Register(devices, clock, officeB, Guid.NewGuid());

        Assert.False(devices.All.Single(d => d.TenantId == officeA).IsActive);
        Assert.True(devices.All.Single(d => d.TenantId == officeB).IsActive);
    }

    private static Task Register(InMemoryPushDevices devices, MovableClock clock, Guid tenantId, Guid sessionId) =>
        RegisterPushDeviceTokenHandler.Handle(
            new RegisterPushDeviceTokenCommand(
                tenantId,
                UserId,
                PushPlatform.Web,
                "same-browser-token",
                null,
                sessionId
            ),
            devices,
            new NoOpUnitOfWork(),
            clock,
            default
        );

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public DateTime UtcNow => now.UtcDateTime;

        public void Advance(TimeSpan by) => now = now.Add(by);

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
