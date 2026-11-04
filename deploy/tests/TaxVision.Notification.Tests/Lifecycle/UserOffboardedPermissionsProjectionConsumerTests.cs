using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Tests.Lifecycle;

/// <summary>El retirado sale de las audiencias ByPermission igual que el suspendido.</summary>
public sealed class UserOffboardedPermissionsProjectionConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Offboarding_marks_the_projection_inactive()
    {
        var userId = Guid.NewGuid();
        var projection = NotificationRecipientPermissionsProjection.Create(
            TenantId,
            userId,
            1,
            ["customers.view"],
            [Guid.NewGuid()]
        );
        var unitOfWork = new CountingUnitOfWork();

        await UserOffboardedPermissionsProjectionConsumer.Handle(
            Offboarded(userId),
            new FakePermissions(projection),
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.False(projection.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Missing_projection_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await UserOffboardedPermissionsProjectionConsumer.Handle(
            Offboarded(Guid.NewGuid()),
            new FakePermissions(),
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static UserOffboardedIntegrationEvent Offboarded(Guid userId) =>
        new()
        {
            TenantId = TenantId,
            UserId = userId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
        };

    private sealed class FakePermissions(params NotificationRecipientPermissionsProjection[] seed)
        : INotificationRecipientPermissionsProjectionRepository
    {
        public Task<NotificationRecipientPermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(seed.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

        public Task AddAsync(NotificationRecipientPermissionsProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> FindActiveUserIdsByPermissionAsync(
            Guid tenantId,
            string permissionCode,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> FilterActiveWithCapabilityAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> userIds,
            string? requiredCapability,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<NotificationRecipientPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<NotificationRecipientPermissionsProjection>>([]);
    }
}
