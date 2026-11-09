using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// AUTH-7: caracteriza cada salida del login (respuesta, auditoría, guardados y avisos) antes de partirlo.
/// La llamada al handler vive en un solo lugar, <see cref="LoginWorld.LoginAsync"/>.
/// </summary>
public sealed class LoginHandlerPathsTests
{
    [Fact]
    public async Task An_ip_over_its_limit_is_refused_without_touching_anything()
    {
        var world = new LoginWorld();
        world.Throttler.IpRetryAfter = TimeSpan.FromSeconds(30);

        var result = await world.LoginAsync();

        Assert.Equal("Auth.LockedOut", result.Error.Code);
        Assert.Empty(world.Account.Audit.Logs);
        Assert.Equal(0, world.UnitOfWork.Saves);
        Assert.Equal(0, world.Throttler.Failures);
    }

    [Fact]
    public async Task An_unknown_tenant_answers_like_a_bad_password_and_audits_the_real_reason()
    {
        var world = new LoginWorld();

        var result = await world.LoginAsync(tenantId: Guid.NewGuid());

        Assert.Equal("Auth.Invalid", result.Error.Code);
        world.AssertAudit(
            AuthAuditAction.LoginFailed,
            false,
            """{"reason":"tenant_inactive_or_missing"}""",
            userId: null
        );
        Assert.Equal(1, world.Throttler.Failures);
        Assert.Equal(1, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task An_inactive_tenant_is_refused_like_an_unknown_one()
    {
        var world = new LoginWorld();
        world.Account.Tenant.SetActive(false);

        var result = await world.LoginAsync();

        Assert.Equal("Auth.Invalid", result.Error.Code);
        world.AssertAudit(
            AuthAuditAction.LoginFailed,
            false,
            """{"reason":"tenant_inactive_or_missing"}""",
            userId: null
        );
    }

    [Fact]
    public async Task An_unknown_email_is_audited_without_a_user()
    {
        var world = new LoginWorld();

        var result = await world.LoginAsync(email: "nobody@example.com");

        Assert.Equal("Auth.Invalid", result.Error.Code);
        world.AssertAudit(AuthAuditAction.LoginFailed, false, """{"reason":"unknown_email"}""", userId: null);
        Assert.Equal(1, world.Throttler.Failures);
        Assert.Equal(1, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task The_email_is_trimmed_and_lowercased_before_looking_for_the_account()
    {
        var world = new LoginWorld();

        var result = await world.LoginAsync(email: "  ADA@example.com ");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_locked_account_is_refused_with_the_time_left_and_does_not_count_against_the_ip()
    {
        var world = new LoginWorld();
        world.User.RegisterFailedLogin(DateTime.UtcNow, 1, TimeSpan.FromMinutes(10));

        var result = await world.LoginAsync();

        Assert.Equal("Auth.LockedOut", result.Error.Code);
        Assert.Contains("temporarily locked", result.Error.Message);
        world.AssertAudit(AuthAuditAction.LoginLockedOut, false, null, world.User.Id);
        Assert.Equal(0, world.Throttler.Failures);
        Assert.Equal(1, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_bad_password_counts_the_failure_and_does_not_alert_below_the_limit()
    {
        var world = new LoginWorld();

        var result = await world.LoginAsync(password: "wrong");

        Assert.Equal("Auth.Invalid", result.Error.Code);
        world.AssertAudit(AuthAuditAction.LoginFailed, false, """{"reason":"bad_password"}""", world.User.Id);
        Assert.Equal(1, world.User.FailedLoginCount);
        Assert.Equal(1, world.Throttler.Failures);
        Assert.Equal(1, world.UnitOfWork.Saves);
        Assert.Equal(1, world.Lock.Acquired);
        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task The_failure_that_locks_the_account_alerts_once_and_after_saving()
    {
        var world = new LoginWorld();
        for (var attempt = 1; attempt < LockoutPolicy.MaxFailedAttempts; attempt++)
            world.User.RegisterFailedLogin(
                DateTime.UtcNow,
                LockoutPolicy.MaxFailedAttempts,
                LockoutPolicy.LockoutDuration
            );

        var result = await world.LoginAsync(password: "wrong");

        Assert.Equal("Auth.Invalid", result.Error.Code);
        Assert.True(world.User.IsLockedOut(DateTime.UtcNow));
        var alert = Assert.IsType<SecurityAlertIntegrationEvent>(Assert.Single(world.Bus.Published));
        Assert.Equal(SecurityAlertType.AccountLockedOut, alert.AlertType);
        Assert.Equal(world.User.Id, alert.UserId);
        Assert.Equal(world.User.TenantId, alert.TenantId);
        Assert.Equal(world.Account.Request.IpAddress, alert.IpAddress);
        Assert.Equal(world.Account.Request.UserAgent, alert.UserAgent);
        Assert.Equal(world.Account.Correlation.CorrelationId, alert.CorrelationId);
        Assert.Equal(0, world.UnitOfWork.PublishedBeforeLastSave);
    }

    [Fact]
    public async Task An_inactive_user_with_the_right_password_is_refused()
    {
        var world = new LoginWorld();
        world.User.Deactivate(Guid.NewGuid(), DateTime.UtcNow);

        var result = await world.LoginAsync();

        Assert.Equal("Auth.Invalid", result.Error.Code);
        world.AssertAudit(AuthAuditAction.LoginFailed, false, """{"reason":"user_inactive"}""", world.User.Id);
        Assert.Equal(0, world.Throttler.Failures);
    }

    // CUS-10: el portal recibe el motivo para avisar al cliente; solo después de validar la contraseña.
    [Fact]
    public async Task An_inactive_portal_account_with_the_right_password_gets_the_inactive_code()
    {
        var world = new LoginWorld(UserActorType.CustomerPortal);
        world.User.ApplyCustomerArchived(DateTime.UtcNow, DateTime.UtcNow);

        var result = await world.LoginAsync();

        Assert.Equal("Auth.UserInactive", result.Error.Code);
        world.AssertAudit(AuthAuditAction.LoginFailed, false, """{"reason":"user_inactive"}""", world.User.Id);
        Assert.Equal(0, world.Throttler.Failures);
    }

    [Fact]
    public async Task An_inactive_portal_account_with_a_wrong_password_still_gets_the_generic_code()
    {
        var world = new LoginWorld(UserActorType.CustomerPortal);
        world.User.ApplyCustomerArchived(DateTime.UtcNow, DateTime.UtcNow);

        var result = await world.LoginAsync(password: "wrong");

        Assert.Equal("Auth.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task A_blocked_office_refuses_its_employees_with_the_renewal_message()
    {
        var world = new LoginWorld();
        world.Account.Tenant.SetBillingAccess(true, "Expired");

        var result = await world.LoginAsync();

        Assert.Equal("Auth.SubscriptionInactive", result.Error.Code);
        Assert.Equal("This office's subscription is inactive. Please renew to restore access.", result.Error.Message);
        world.AssertAudit(AuthAuditAction.LoginFailed, false, """{"reason":"subscription_inactive"}""", world.User.Id);
    }

    [Fact]
    public async Task A_blocked_office_lets_its_admin_in_to_renew()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin);
        world.Account.Tenant.SetBillingAccess(true, "Expired");

        var result = await world.LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Tokens);
    }

    [Fact]
    public async Task A_good_password_clears_previous_failures_under_the_lock()
    {
        var world = new LoginWorld();
        world.User.RegisterFailedLogin(DateTime.UtcNow, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);

        var result = await world.LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, world.User.FailedLoginCount);
        Assert.Equal(1, world.Lock.Acquired);
    }

    [Fact]
    public async Task A_clean_account_takes_no_lock_and_gets_tokens()
    {
        var world = new LoginWorld();

        var result = await world.LoginAsync();

        Assert.False(result.Value.MfaRequired);
        Assert.False(result.Value.MfaSetupRequired);
        Assert.False(result.Value.TakeoverRequired);
        Assert.NotNull(result.Value.Tokens);
        Assert.Null(result.Value.Tokens!.DeviceToken);
        Assert.Equal(UserActorType.TenantEmployee.ToString(), result.Value.ActorType);
        world.AssertAudit(AuthAuditAction.LoginSucceeded, true, null, world.User.Id);
        Assert.Equal(0, world.Lock.Acquired);
        Assert.Equal(1, world.UnitOfWork.Saves);
        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task An_open_session_asks_for_a_takeover_instead_of_tokens()
    {
        var world = new LoginWorld();
        await world.Account.StartWorkspaceSessionAsync();

        var result = await world.LoginAsync();

        Assert.True(result.Value.TakeoverRequired);
        Assert.Null(result.Value.Tokens);
        Assert.False(result.Value.MfaSetupRequired);
        Assert.Equal(world.Tickets.Issued.ToString(), result.Value.TakeoverTicket);
        Assert.Equal(
            (int)LockoutPolicy.TakeoverTicketValidity.TotalSeconds,
            result.Value.TakeoverTicketExpiresInSeconds
        );
        Assert.Equal(UserActorType.TenantEmployee.ToString(), result.Value.ActorType);
        Assert.False(world.Tickets.LastPayload!.MustEnrollMfa);
        world.AssertAudit(AuthAuditAction.LoginSucceeded, true, """{"takeoverRequired":true}""", world.User.Id);
        Assert.Equal(1, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Required_mfa_without_a_method_lets_in_with_the_setup_flag()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);

        var result = await world.LoginAsync(deviceName: "Laptop");

        Assert.True(result.Value.MfaSetupRequired);
        Assert.False(result.Value.MfaRequired);
        Assert.NotNull(result.Value.Tokens);
        Assert.Equal(UserActorType.TenantAdmin.ToString(), result.Value.ActorType);
        world.AssertAudit(AuthAuditAction.LoginSucceeded, true, """{"mfaSetupRequired":true}""", world.User.Id);
    }

    [Fact]
    public async Task Required_mfa_without_a_method_and_an_open_session_keeps_the_setup_flag_in_the_ticket()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        await world.Account.StartWorkspaceSessionAsync();

        var result = await world.LoginAsync(deviceName: "Laptop");

        Assert.True(result.Value.TakeoverRequired);
        Assert.True(result.Value.MfaSetupRequired);
        Assert.True(world.Tickets.LastPayload!.MustEnrollMfa);
        Assert.Equal("Laptop", world.Tickets.LastPayload.DeviceName);
        world.AssertAudit(
            AuthAuditAction.LoginSucceeded,
            true,
            """{"takeoverRequired":true,"mfaSetupRequired":true}""",
            world.User.Id
        );
    }

    [Fact]
    public async Task A_trusted_device_skips_the_second_factor()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.Mfa.Enroll(world.User);
        world.TrustDevice(world.User.Id);

        var result = await world.LoginAsync(deviceToken: "device-1");

        Assert.False(result.Value.MfaRequired);
        Assert.False(result.Value.MfaSetupRequired);
        Assert.NotNull(result.Value.Tokens);
        Assert.Empty(world.Mfa.Challenges);
        world.AssertAudit(AuthAuditAction.LoginSucceeded, true, """{"trustedDevice":true}""", world.User.Id);
    }

    [Fact]
    public async Task A_trusted_device_with_an_open_session_asks_for_a_takeover()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.Mfa.Enroll(world.User);
        world.TrustDevice(world.User.Id);
        await world.Account.StartWorkspaceSessionAsync();

        var result = await world.LoginAsync(deviceToken: "device-1");

        Assert.True(result.Value.TakeoverRequired);
        Assert.False(result.Value.MfaSetupRequired);
        world.AssertAudit(
            AuthAuditAction.LoginSucceeded,
            true,
            """{"takeoverRequired":true,"trustedDevice":true}""",
            world.User.Id
        );
    }

    [Fact]
    public async Task Someone_elses_device_does_not_skip_the_second_factor()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.Mfa.Enroll(world.User);
        world.TrustDevice(Guid.NewGuid());

        var result = await world.LoginAsync(deviceToken: "device-1");

        Assert.True(result.Value.MfaRequired);
        Assert.Single(world.Mfa.Challenges);
    }

    [Fact]
    public async Task A_totp_challenge_sends_nothing_and_answers_with_the_ticket()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.Mfa.Enroll(world.User);

        var result = await world.LoginAsync();

        Assert.True(result.Value.MfaRequired);
        Assert.Null(result.Value.Tokens);
        Assert.Equal("raw-1", result.Value.LoginTicket);
        Assert.Equal(["Totp"], result.Value.MfaMethods!);
        Assert.Equal((int)LockoutPolicy.MfaTicketValidity.TotalSeconds, result.Value.TicketExpiresInSeconds);
        var challenge = Assert.Single(world.Mfa.Challenges);
        Assert.Equal("hash:raw-1", challenge.LoginTicketHash);
        Assert.Null(challenge.OtpHash);
        Assert.Equal(world.Mfa.Methods[0].Id, challenge.MfaMethodId);
        world.AssertAudit(AuthAuditAction.MfaChallengeSent, true, """{"method":"Totp"}""", world.User.Id);
        Assert.Empty(world.Bus.Published);
        Assert.Empty(world.Account.Sessions.Sessions);
    }

    [Fact]
    public async Task An_email_challenge_sends_the_code_after_saving()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.User.EnableMfa();
        var method = world.AddMethod(MfaMethodType.Email, destination: "otp@example.com");

        var result = await world.LoginAsync();

        Assert.True(result.Value.MfaRequired);
        var otp = Assert.IsType<MfaChallengeRequestedIntegrationEvent>(Assert.Single(world.Bus.Published));
        Assert.Equal("Email", otp.Channel);
        Assert.Equal("otp@example.com", otp.Destination);
        Assert.Equal("123456", otp.Code);
        Assert.Equal("login", otp.Purpose);
        Assert.Equal(world.User.Id, otp.UserId);
        Assert.Equal(world.Account.Correlation.CorrelationId, otp.CorrelationId);
        Assert.Equal("hash:123456", Assert.Single(world.Mfa.Challenges).OtpHash);
        Assert.Equal(method.Id, world.Mfa.Challenges[0].MfaMethodId);
        Assert.Equal(0, world.UnitOfWork.PublishedBeforeLastSave);
        Assert.Equal(1, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task The_challenge_prefers_the_marked_method_and_then_totp()
    {
        var preferred = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        preferred.Mfa.Enroll(preferred.User);
        var sms = preferred.AddMethod(MfaMethodType.Sms, destination: "+15550100");
        sms.MarkPreferred(true);

        await preferred.LoginAsync();

        Assert.Equal(sms.Id, preferred.Mfa.Challenges[0].MfaMethodId);
        var otp = Assert.IsType<MfaChallengeRequestedIntegrationEvent>(Assert.Single(preferred.Bus.Published));
        Assert.Equal("Sms", otp.Channel);
        Assert.Equal("+15550100", otp.Destination);

        var totpFirst = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        totpFirst.User.EnableMfa();
        totpFirst.AddMethod(MfaMethodType.Email, destination: "otp@example.com");
        totpFirst.Mfa.Enroll(totpFirst.User);

        var result = await totpFirst.LoginAsync();

        Assert.Equal(totpFirst.Mfa.Methods[1].Id, totpFirst.Mfa.Challenges[0].MfaMethodId);
        Assert.Equal(["Email", "Totp"], result.Value.MfaMethods!);
    }

    [Fact]
    public async Task An_unconfirmed_method_does_not_count_as_enrolled()
    {
        var world = new LoginWorld(UserActorType.TenantAdmin, mfaEnforced: true);
        world.AddMethod(MfaMethodType.Email, destination: "otp@example.com", confirmed: false);

        var result = await world.LoginAsync();

        Assert.True(result.Value.MfaSetupRequired);
        Assert.Empty(world.Mfa.Challenges);
    }

    private sealed class LoginWorld
    {
        private readonly bool _mfaEnforced;

        public LoginWorld(UserActorType actorType = UserActorType.TenantEmployee, bool mfaEnforced = false)
        {
            _mfaEnforced = mfaEnforced;
            Account = new AccountSessionFixture(actorType);
            UnitOfWork = new OrderProbeUnitOfWork(Bus);
        }

        public AccountSessionFixture Account { get; }
        public User User => Account.Admin;
        public InMemoryMfaRepository Mfa { get; } = new();
        public CountingLoginThrottler Throttler { get; } = new();
        public CountingLoginLock Lock { get; } = new();
        public RecordingTakeoverTickets Tickets { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public OrderProbeUnitOfWork UnitOfWork { get; }

        public MfaMethod AddMethod(MfaMethodType type, string? destination, bool confirmed = true)
        {
            var method = MfaMethod.Create(User.TenantId, User.Id, type, null, destination).Value;
            if (confirmed)
                method.Confirm();
            Mfa.Methods.Add(method);
            return method;
        }

        public void TrustDevice(Guid ownerId) =>
            Mfa.Devices.Add(
                TrustedDevice.Create(User.TenantId, ownerId, "hash:device-1", "Chrome", TimeSpan.FromDays(30))
            );

        public void AssertAudit(string action, bool success, string? details, Guid? userId)
        {
            var log = Assert.Single(Account.Audit.Logs);
            Assert.Equal(action, log.Action);
            Assert.Equal(success, log.Success);
            Assert.Equal(details, log.DetailsJson);
            Assert.Equal(userId, log.UserId);
            Assert.Equal(Account.Request.IpAddress, log.IpAddress);
            Assert.Equal(Account.Request.UserAgent, log.UserAgent);
            Assert.Equal(Account.Correlation.CorrelationId, log.CorrelationId);
        }

        public Task<Result<LoginResponse>> LoginAsync(
            string password = "secret",
            string? email = null,
            Guid? tenantId = null,
            string? deviceName = null,
            string? deviceToken = null
        )
        {
            var steps = LoginStepsFixture.Build(
                Account,
                Lock,
                Mfa,
                Tickets,
                Throttler,
                new MfaOptions { Enforced = _mfaEnforced }
            );
            return LoginHandler.Handle(
                new LoginCommand(tenantId ?? Account.Tenant.Id, email ?? User.Email, password, deviceName, deviceToken),
                steps.Discovery,
                steps.Password,
                steps.Mfa,
                steps.Session,
                UnitOfWork,
                Bus,
                CancellationToken.None
            );
        }
    }

    private sealed class OrderProbeUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PublishedBeforeLastSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            PublishedBeforeLastSave = bus.Published.Count;
            return Task.FromResult(1);
        }
    }

    private sealed class CountingLoginLock : IUserLoginLock
    {
        public int Acquired { get; private set; }

        public Task AcquireAsync(User user, CancellationToken ct)
        {
            Acquired++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTakeoverTickets : ISessionTakeoverTicketStore
    {
        public Guid Issued { get; } = Guid.NewGuid();
        public SessionTakeoverPayload? LastPayload { get; private set; }

        public Task<Guid> IssueAsync(SessionTakeoverPayload payload, CancellationToken ct = default)
        {
            LastPayload = payload;
            return Task.FromResult(Issued);
        }

        public Task<SessionTakeoverPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default) =>
            Task.FromResult<SessionTakeoverPayload?>(null);
    }

    private sealed class CountingLoginThrottler : ILoginThrottler
    {
        private readonly PermissiveLoginThrottler _inner = new();

        public TimeSpan? IpRetryAfter { get; set; }
        public int Failures { get; private set; }

        public Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.FromResult(IpRetryAfter);

        public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default)
        {
            Failures++;
            return Task.CompletedTask;
        }

        public Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) =>
            _inner.IsOtpResendThrottledAsync(userId, ct);

        public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) =>
            _inner.RegisterOtpSentAsync(userId, ct);

        public Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
            string email,
            string? ipAddress,
            CancellationToken ct = default
        ) => _inner.GetPasswordResetRetryAfterAsync(email, ipAddress, ct);

        public Task RegisterPasswordResetRequestAsync(
            string email,
            string? ipAddress,
            CancellationToken ct = default
        ) => _inner.RegisterPasswordResetRequestAsync(email, ipAddress, ct);

        public Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            _inner.GetInvitationAcceptRetryAfterAsync(ipAddress, ct);

        public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
            _inner.RegisterInvitationAcceptAttemptAsync(ipAddress, ct);

        public Task<Result> AuthorizeOnboardingChallengeCreationAsync(
            string email,
            string ipAddress,
            CancellationToken ct = default
        ) => _inner.AuthorizeOnboardingChallengeCreationAsync(email, ipAddress, ct);

        public Task<Result> AuthorizeOnboardingResendAsync(Guid challengeId, CancellationToken ct = default) =>
            _inner.AuthorizeOnboardingResendAsync(challengeId, ct);
    }
}
