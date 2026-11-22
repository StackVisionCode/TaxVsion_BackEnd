using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Push.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Al terminar una sesión se apaga el push que se registró con ella, aunque el navegador no avisara.</summary>
public sealed class SessionEndedPushDeviceConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid S1 = Guid.NewGuid();
    private static readonly Guid S2 = Guid.NewGuid();

    [Fact]
    public async Task Revokes_only_the_device_of_that_session()
    {
        var devices = new InMemoryPushDevices();
        var first = Device(devices, "token-1", S1);
        var second = Device(devices, "token-2", S2);

        await Handle(devices, Ended(sessionId: S1));

        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
    }

    [Fact]
    public async Task User_scope_keeps_the_excepted_session()
    {
        var devices = new InMemoryPushDevices();
        var kept = Device(devices, "token-1", S1);
        var other = Device(devices, "token-2", S2);
        var legacy = Device(devices, "token-3", sessionId: null);

        await Handle(devices, Ended(sessionId: null, exceptSessionId: S1));

        Assert.True(kept.IsActive);
        Assert.False(other.IsActive);
        Assert.False(legacy.IsActive);
    }

    [Fact]
    public async Task Late_event_does_not_revoke_a_device_re_registered_by_a_new_session()
    {
        var devices = new InMemoryPushDevices();
        var device = Device(devices, "token-1", S1);
        var s3 = Guid.NewGuid();
        device.Reactivate(UserId, PushPlatform.Web, null, s3);

        await Handle(devices, Ended(sessionId: S1));

        Assert.True(device.IsActive);
    }

    [Fact]
    public async Task Unknown_session_is_a_no_op()
    {
        var devices = new InMemoryPushDevices();
        var device = Device(devices, "token-1", S1);
        var unitOfWork = new CountingUnitOfWork();

        await SessionEndedPushDeviceConsumer.Handle(Ended(sessionId: Guid.NewGuid()), devices, unitOfWork, default);

        Assert.True(device.IsActive);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static PushDeviceToken Device(InMemoryPushDevices devices, string token, Guid? sessionId)
    {
        var device = PushDeviceToken.Register(TenantId, UserId, PushPlatform.Web, token, null, sessionId).Value;
        devices.All.Add(device);
        return device;
    }

    private static Task Handle(InMemoryPushDevices devices, SessionEndedIntegrationEvent evt) =>
        SessionEndedPushDeviceConsumer.Handle(evt, devices, new CountingUnitOfWork(), default);

    private static SessionEndedIntegrationEvent Ended(Guid? sessionId, Guid? exceptSessionId = null) =>
        new()
        {
            TenantId = TenantId,
            UserId = UserId,
            SessionId = sessionId,
            ExceptSessionId = exceptSessionId,
            Reason = "user_logout",
            EndedAtUtc = DateTime.UtcNow,
        };

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(0);
        }
    }
}
