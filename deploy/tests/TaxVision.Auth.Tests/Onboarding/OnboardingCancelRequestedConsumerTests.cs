using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Consumers;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow (Fase 17) — compensación de un onboarding cancelado que ya había creado el
/// usuario dueño del tenant.</summary>
public sealed class OnboardingCancelRequestedConsumerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static User NewUser(Guid onboardingId) =>
        User.Register(
            Guid.NewGuid(),
            "Ada",
            "Lovelace",
            "ada@castillotax.com",
            "hash",
            UserActorType.TenantAdmin,
            customerId: null,
            onboardingId: onboardingId
        ).Value;

    [Fact]
    public async Task Deactivates_the_user_when_it_exists_for_the_onboarding()
    {
        var onboardingId = Guid.NewGuid();
        var user = NewUser(onboardingId);
        var users = new FakeOnboardingUserRepository { Existing = user };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = onboardingId,
            Reason = "Provisioning failed permanently",
            OnboardingTenantId = Guid.NewGuid(),
            OnboardingUserId = user.Id,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            users,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<User>.Instance,
            CancellationToken.None
        );

        Assert.False(user.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Is_a_noop_when_the_tenant_admin_step_never_ran()
    {
        var users = new FakeOnboardingUserRepository();
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            Reason = "Provisioning failed at Tenant step",
            OnboardingTenantId = null,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            users,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<User>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Is_idempotent_when_the_user_is_already_deactivated()
    {
        var onboardingId = Guid.NewGuid();
        var user = NewUser(onboardingId);
        user.Deactivate(Now);
        var users = new FakeOnboardingUserRepository { Existing = user };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = onboardingId,
            Reason = "Replay of an already-processed cancel",
            OnboardingTenantId = Guid.NewGuid(),
            OnboardingUserId = user.Id,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            users,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<User>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeOnboardingUserRepository : IUserRepository
    {
        public User? Existing { get; set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
