using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Tests.Application;

/// <summary>NOT-04 pasos 2 y 3: los eventos con enlace de cuenta llevan el token protegido y, si lo hay, no el claro.</summary>
public sealed class AccountTokenProtectionTests
{
    [Fact]
    public async Task Password_reset_event_carries_the_protected_token()
    {
        var world = new AccountSessionFixture();
        var protector = new CapturingAccountTokenProtector();
        var bus = new FakeMessageBus();

        await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(world.Admin.Email),
            world.Users,
            world.Tenants,
            new InMemoryCredentialTokenRepository(),
            world.TokenService,
            protector,
            new PermissiveLoginThrottler(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        var evt = Assert.Single(bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        AssertProtected(
            evt.ProtectedToken,
            evt.RawToken,
            AuthTokenPayloadSchemas.PasswordReset,
            AuthTokenPayloadSchemas.PasswordResetBinding(world.Tenant.Id, world.Admin.Id),
            protector
        );
    }

    [Fact]
    public async Task Email_change_event_carries_the_protected_token()
    {
        var world = new AccountSessionFixture();
        var protector = new CapturingAccountTokenProtector();
        var bus = new FakeMessageBus();

        var result = await RequestEmailChangeHandler.Handle(
            new RequestEmailChangeCommand(world.Admin.Id, "ada.new@example.com"),
            world.Users,
            world.Tenants,
            new EmailVerificationCapture(),
            world.TokenService,
            protector,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(bus.Published.OfType<EmailChangeRequestedIntegrationEvent>());
        AssertProtected(
            evt.ProtectedToken,
            evt.RawToken,
            AuthTokenPayloadSchemas.EmailChange,
            AuthTokenPayloadSchemas.EmailChangeBinding(world.Tenant.Id, world.Admin.Id),
            protector
        );
    }

    [Fact]
    public async Task Invitation_event_carries_the_protected_token()
    {
        var world = new AccountSessionFixture();
        var protector = new CapturingAccountTokenProtector();
        var bus = new FakeMessageBus();

        var result = await IssueCustomerPortalInvitationHandler.Handle(
            new IssueCustomerPortalInvitationCommand(
                world.Tenant.Id,
                Guid.NewGuid(),
                "ana@example.com",
                world.Admin.Id
            ),
            world.Users,
            world.Tenants,
            new InMemoryInvitationRepository(),
            new SequentialInvitationTokenService(),
            protector,
            world.Audit,
            world.Request,
            world.Correlation,
            Options.Create(new InvitationOptions()),
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(bus.Published.OfType<InvitationCreatedIntegrationEvent>());
        AssertProtected(
            evt.ProtectedToken,
            evt.RawToken,
            AuthTokenPayloadSchemas.Invitation,
            AuthTokenPayloadSchemas.InvitationBinding(world.Tenant.Id, evt.InvitationId),
            protector
        );
    }

    [Fact]
    public async Task Password_reset_event_falls_back_to_the_raw_token_when_it_cannot_be_protected()
    {
        var world = new AccountSessionFixture();
        var bus = new FakeMessageBus();

        await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(world.Admin.Email),
            world.Users,
            world.Tenants,
            new InMemoryCredentialTokenRepository(),
            world.TokenService,
            new NullAccountTokenProtector(),
            new PermissiveLoginThrottler(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );

        var evt = Assert.Single(bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Null(evt.ProtectedToken);
        Assert.False(string.IsNullOrEmpty(evt.RawToken));
    }

    private static void AssertProtected(
        ProtectedPayload? protectedToken,
        string? rawToken,
        string schema,
        ProtectedPayloadBinding binding,
        CapturingAccountTokenProtector protector
    )
    {
        Assert.NotNull(protectedToken);
        var call = Assert.Single(protector.Calls);
        Assert.Equal(schema, call.Schema);
        Assert.Equal(binding, call.Binding);
        Assert.Equal($"protected:{call.RawToken}", protectedToken!.Cipher);
        // Paso 3: con el token protegido, el claro no viaja.
        Assert.Null(rawToken);
    }

    internal sealed class CapturingAccountTokenProtector : IAccountTokenProtector
    {
        public List<(string Schema, ProtectedPayloadBinding Binding, string RawToken)> Calls { get; } = [];

        public ProtectedPayload? Protect(string schema, ProtectedPayloadBinding binding, string rawToken)
        {
            Calls.Add((schema, binding, rawToken));
            return new ProtectedPayload
            {
                Cipher = $"protected:{rawToken}",
                KeyVersion = 1,
                Schema = schema,
            };
        }
    }

    private sealed class EmailVerificationCapture : ICredentialTokenRepository
    {
        public Task AddEmailVerificationAsync(EmailVerificationToken token, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PasswordResetToken?> GetPasswordResetByHashAsync(
            string tokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<PasswordResetToken>> GetPendingPasswordResetsAsync(
            Guid userId,
            DateTime utcNow,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(
            string tokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddPhoneVerificationAsync(PhoneVerificationToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
