using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Push.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Lifecycle;

/// <summary>Suspender o retirar revoca todos los dispositivos push del usuario, y solo los suyos.</summary>
public sealed class UserLifecyclePushDeviceConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Deactivation_revokes_every_active_device_of_the_user()
    {
        var userId = Guid.NewGuid();
        var devices = new FakeDevices();
        var mine = new[] { devices.Add(userId, "t-1"), devices.Add(userId, "t-2") };
        var other = devices.Add(Guid.NewGuid(), "t-3");
        var unitOfWork = new CountingUnitOfWork();

        await UserLifecyclePushDeviceConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "suspended@example.com",
                ActorType = "TenantEmployee",
            },
            devices,
            unitOfWork,
            new NoOpCorrelation(),
            NullLogger<PushDeviceToken>.Instance,
            CancellationToken.None
        );

        Assert.All(mine, token => Assert.False(token.IsActive));
        Assert.True(other.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Offboarding_revokes_every_active_device_and_redelivery_is_a_noop()
    {
        var userId = Guid.NewGuid();
        var devices = new FakeDevices();
        var token = devices.Add(userId, "t-1");
        var unitOfWork = new CountingUnitOfWork();
        var evt = new UserOffboardedIntegrationEvent
        {
            TenantId = TenantId,
            UserId = userId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
        };

        for (var i = 0; i < 2; i++)
            await UserLifecyclePushDeviceConsumer.Handle(
                evt,
                devices,
                unitOfWork,
                new NoOpCorrelation(),
                NullLogger<PushDeviceToken>.Instance,
                CancellationToken.None
            );

        Assert.False(token.IsActive);
        Assert.NotNull(token.RevokedAtUtc);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private sealed class FakeDevices : IPushDeviceTokenRepository
    {
        private readonly List<PushDeviceToken> _tokens = [];

        public PushDeviceToken Add(Guid userId, string token)
        {
            var created = PushDeviceToken.Register(TenantId, userId, PushPlatform.Fcm, token, deviceId: null).Value;
            _tokens.Add(created);
            return created;
        }

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            var active = _tokens.Where(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive).ToList();
            active.ForEach(t => t.Revoke());
            return Task.FromResult(active.Count);
        }

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>(
                _tokens.Where(t => t.UserId == userId && t.IsActive).ToList()
            );

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
