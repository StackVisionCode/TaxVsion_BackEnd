using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>La regla del último admin cuenta bajo el candado del tenant: primero el candado, después la cuenta.</summary>
public sealed class DeactivateUserHandlerTests
{
    [Fact]
    public async Task TargetAdmin_AcquiresTenantLockBeforeCounting()
    {
        var tenantId = Guid.NewGuid();
        var target = User.Register(tenantId, "Tania", "Ruiz", "tania@example.com", "hash", UserActorType.TenantAdmin).Value;
        var calls = new List<string>();

        var result = await DeactivateUserHandler.Handle(
            new DeactivateUserCommand(tenantId, target.Id, Guid.NewGuid(), UserActorType.TenantAdmin),
            new OrderRecordingUserRepository(target, calls, activeAdmins: 1),
            new RecordingTenantAdminLock(calls),
            new SessionAccessCutoff(new EmptyUserSessionRepository(), new RecordingAccessTokenDenylist(), new QuietRevocations()),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal("User.LastAdmin", result.Error.Code);
        Assert.Equal([$"lock:{tenantId}", "count"], calls);
    }

    // La regla sale del agregado pero el error que ve el usuario no cambia: va antes que la del último admin.
    [Fact]
    public async Task LastAdminDeactivatingSelf_StillGetsSelfAction_WithoutTakingTheLock()
    {
        var tenantId = Guid.NewGuid();
        var self = User.Register(tenantId, "Tania", "Ruiz", "tania@example.com", "hash", UserActorType.TenantAdmin).Value;
        var calls = new List<string>();

        var result = await DeactivateUserHandler.Handle(
            new DeactivateUserCommand(tenantId, self.Id, self.Id, UserActorType.TenantAdmin),
            new OrderRecordingUserRepository(self, calls, activeAdmins: 1),
            new RecordingTenantAdminLock(calls),
            new SessionAccessCutoff(new EmptyUserSessionRepository(), new RecordingAccessTokenDenylist(), new QuietRevocations()),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal("User.SelfAction", result.Error.Code);
        Assert.Empty(calls);
        Assert.True(self.IsActive);
    }

    private sealed class RecordingTenantAdminLock(List<string> calls) : ITenantAdminLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken ct)
        {
            calls.Add($"lock:{tenantId}");
            return Task.CompletedTask;
        }
    }

    private sealed class QuietRevocations : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class OrderRecordingUserRepository(User target, List<string> calls, int activeAdmins)
        : IUserRepository
    {
        public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<PagedResult<TenantOwnerReconciliationResponse>> ListTenantOwnersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<TenantOwnerReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<CustomerPortalAccountReconciliationResponse>> ListPortalAccountsForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<CustomerPortalAccountReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<StaffUserReconciliationResponse>> ListStaffUsersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<StaffUserReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<UserDirectoryReconciliationResponse>> ListUsersForDirectoryReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<UserDirectoryReconciliationResponse>([], page, size, 0));

        public Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default)
        {
            calls.Add("count");
            return Task.FromResult(activeAdmins);
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<User?>(id == target.Id ? target : null);

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
