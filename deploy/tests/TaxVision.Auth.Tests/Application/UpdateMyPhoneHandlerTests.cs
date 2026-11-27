using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

// Teléfono propio sin OTP: se guarda directo, vacío lo quita y nunca queda marcado como verificado.
public sealed class UpdateMyPhoneHandlerTests
{
    [Fact]
    public async Task Saves_the_phone_without_marking_it_verified_and_audits()
    {
        var user = NewUser();
        var audit = new RecordingAudit();
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(user, " +1 809-555-0100 ", audit, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal("+18095550100", user.PhoneNumber);
        Assert.False(user.PhoneVerified);
        Assert.Equal(AuthAuditAction.PhoneUpdated, Assert.Single(audit.Logs).Action);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Empty_value_removes_the_phone()
    {
        var user = NewUser();
        user.SetPhoneNumber("+18095550100");

        var result = await HandleAsync(user, "  ", new RecordingAudit(), new CountingUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.Null(user.PhoneNumber);
        Assert.False(user.PhoneVerified);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("phone 8095550100")]
    [InlineData("+1809555010012345678901")]
    public async Task Invalid_phone_is_rejected_and_nothing_is_saved(string phone)
    {
        var user = NewUser();
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(user, phone, new RecordingAudit(), unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Equal("User.Phone", result.Error.Code);
        Assert.Null(user.PhoneNumber);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Inactive_or_missing_user_is_not_found()
    {
        var result = await UpdateMyPhoneHandler.Handle(
            new UpdateMyPhoneCommand(Guid.NewGuid(), "+18095550100"),
            new SingleUserRepository(null),
            new RecordingAudit(),
            new QuietRequest(),
            new QuietCorrelation(),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal("User.NotFound", result.Error.Code);
    }

    // El código por SMS va al mismo E.164 que se guardará al confirmarlo.
    [Fact]
    public async Task Phone_verification_uses_the_e164_number()
    {
        var user = NewUser();
        var credentials = new InMemoryCredentialTokenRepository();
        var bus = new FakeMessageBus();

        var result = await RequestVerificationAsync(user, "(809) 555-0100", credentials, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal("+18095550100", Assert.Single(credentials.PhoneVerifications).PhoneNumber);
        var message = Assert.IsType<MfaChallengeRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("+18095550100", message.Destination);
    }

    [Fact]
    public async Task Phone_verification_rejects_an_invalid_number_without_sending_a_code()
    {
        var user = NewUser();
        var credentials = new InMemoryCredentialTokenRepository();
        var bus = new FakeMessageBus();

        var result = await RequestVerificationAsync(user, "305-555-CALL", credentials, bus);

        Assert.Equal("User.Phone", result.Error.Code);
        Assert.Empty(credentials.PhoneVerifications);
        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result> RequestVerificationAsync(
        User user,
        string phone,
        InMemoryCredentialTokenRepository credentials,
        FakeMessageBus bus
    ) =>
        RequestPhoneVerificationHandler.Handle(
            new RequestPhoneVerificationCommand(user.Id, phone),
            new SingleUserRepository(user),
            credentials,
            new PredictableTokenService(),
            new PermissiveLoginThrottler(),
            new RecordingAudit(),
            new QuietRequest(),
            new QuietCorrelation(),
            new CountingUnitOfWork(),
            bus,
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result> HandleAsync(
        User user,
        string? phone,
        RecordingAudit audit,
        CountingUnitOfWork unitOfWork
    ) =>
        UpdateMyPhoneHandler.Handle(
            new UpdateMyPhoneCommand(user.Id, phone),
            new SingleUserRepository(user),
            audit,
            new QuietRequest(),
            new QuietCorrelation(),
            unitOfWork,
            CancellationToken.None
        );

    private static User NewUser() =>
        User.Register(Guid.NewGuid(), "Elio", "Paz", "elio@example.com", "hash", UserActorType.TenantEmployee).Value;

    private sealed class SingleUserRepository(User? user) : IUserRepository
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

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(user);

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

        public Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) =>
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

    private sealed class RecordingAudit : IAuthAuditWriter
    {
        public List<AuthAuditLog> Logs { get; } = [];

        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class QuietRequest : IRequestContext
    {
        public string? IpAddress => "203.0.113.10";
        public string? UserAgent => "test-agent";
        public string? ActorType => null;
    }

    private sealed class QuietCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
