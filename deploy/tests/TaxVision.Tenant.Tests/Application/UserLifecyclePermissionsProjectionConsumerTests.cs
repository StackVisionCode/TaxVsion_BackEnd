using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Tenant.Application.Consumers;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// Desactivar/retirar deja la proyección de permisos fail-closed (tampoco sirve como responsable de
/// seguridad); reactivar la vuelve a autorizar; no-op si aún no existe.
/// </summary>
public sealed class UserLifecyclePermissionsProjectionConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly InMemoryUserPermissionsProjectionRepository _users = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task UserDeactivated_marks_the_projection_inactive()
    {
        var user = _users.Seed(_tenantId, _userId);

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = _tenantId,
                UserId = _userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            _users,
            _unitOfWork,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.False(user.IsActive);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task UserOffboarded_marks_the_projection_inactive()
    {
        var user = _users.Seed(_tenantId, _userId);

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = _tenantId,
                UserId = _userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            _users,
            _unitOfWork,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task UserReactivated_marks_the_projection_active_again()
    {
        var user = _users.Seed(_tenantId, _userId);
        user.MarkInactive();

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserReactivatedIntegrationEvent
            {
                TenantId = _tenantId,
                UserId = _userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            _users,
            _unitOfWork,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task An_unknown_user_is_a_noop()
    {
        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = _tenantId,
                UserId = _userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            _users,
            _unitOfWork,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}
