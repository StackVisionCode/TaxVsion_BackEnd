using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Corrección del correo laboral de un empleado por el Owner (D-11, 03 §11): link al correo nuevo y aviso al
/// anterior; el correo de login no cambia hasta que el nuevo se verifica.
/// </summary>
public sealed class StaffEmailCorrectionHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result<StaffEmailChangeRequestedResponse>> RequestAsync(User actor, User target, string newEmail) =>
        RequestStaffEmailChangeHandler.Handle(
            new RequestStaffEmailChangeCommand(_office.Tenant.Id, actor.Id, target.Id, newEmail),
            _office.Users,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            _office.Tenants,
            _office.Options,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    [Fact]
    public async Task The_owner_sends_a_link_to_the_new_address_and_the_login_email_does_not_change_yet()
    {
        var previous = _office.Employee.Email;

        var result = await RequestAsync(_office.Owner, _office.Employee, "  New.Address@Example.com ");

        Assert.True(result.IsSuccess);
        Assert.Equal(previous, _office.Employee.Email);
        var verification = Assert.Single(_office.Credentials.EmailVerifications);
        Assert.Equal("new.address@example.com", verification.NewEmail);
        Assert.Equal(_office.Owner.Id, verification.RequestedByUserId);
        Assert.Equal(verification.ExpiresAtUtc, result.Value.ExpiresAtUtc);

        var requested = Assert.Single(_office.Bus.Published.OfType<EmailChangeRequestedIntegrationEvent>());
        Assert.Equal(previous, requested.CurrentEmail);
        Assert.Equal("new.address@example.com", requested.NewEmail);
        Assert.Equal(_office.Employee.Id, requested.UserId);

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.email.change_requested_by_office", log.Action);
        Assert.Equal(_office.Owner.Id, log.UserId);
        Assert.Equal(_office.Employee.Id, log.TargetId);
        Assert.DoesNotContain("new.address", log.DetailsJson);
    }

    [Fact]
    public async Task A_co_admin_is_denied_and_the_denial_is_audited()
    {
        var result = await RequestAsync(_office.CoAdmin, _office.Employee, "other@example.com");

        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", result.Error.Code);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
        Assert.Empty(_office.Credentials.EmailVerifications);
    }

    [Fact]
    public async Task An_address_already_used_in_the_office_is_rejected()
    {
        var result = await RequestAsync(_office.Owner, _office.Employee, _office.CoAdmin.Email);

        Assert.Equal("User.EmailConflict", result.Error.Code);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task The_same_address_is_rejected()
    {
        var result = await RequestAsync(_office.Owner, _office.Employee, _office.Employee.Email);

        Assert.Equal("User.Email", result.Error.Code);
    }

    // El empleado confirma la corrección pedida por el Owner (SR-04).
    private async Task ConfirmOwnerCorrectionAsync()
    {
        _office.Credentials.EmailVerifications.Add(
            EmailVerificationToken.Create(
                _office.Tenant.Id,
                _office.Employee.Id,
                "employee.new@example.com",
                _office.Tokens.Hash("raw-staff-confirm"),
                TimeSpan.FromHours(24),
                requestedByUserId: _office.Owner.Id
            )
        );
        var confirmed = await ConfirmEmailChangeHandler.Handle(
            new ConfirmEmailChangeCommand("raw-staff-confirm"),
            _office.Credentials,
            _office.Tokens,
            _office.Users,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );
        Assert.True(confirmed.IsSuccess);
    }

    [Fact]
    public async Task After_the_employee_confirms_an_owner_correction_links_and_secure_pause_for_72_hours()
    {
        await ConfirmOwnerCorrectionAsync();

        var send = await SendStaffPasswordResetLinkHandler.Handle(
            new SendStaffPasswordResetLinkCommand(_office.Tenant.Id, _office.Owner.Id, _office.Employee.Id),
            _office.Users,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            _office.Tenants,
            new NoUserLoginLock(),
            _office.Options,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );
        var secure = await SecureStaffAccountHandler.Handle(
            new SecureStaffAccountCommand(
                _office.Tenant.Id,
                _office.Owner.Id,
                _office.Employee.Id,
                CredentialAdministrationReasonCode.SuspectedCompromise,
                null
            ),
            _office.Users,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            _office.Tenants,
            new NoUserLoginLock(),
            _office.Cutoff,
            _office.Mfa,
            _office.Options,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );
        var status = await GetStaffCredentialStatusHandler.Handle(
            new GetStaffCredentialStatusQuery(_office.Tenant.Id, _office.Owner.Id, _office.Employee.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

        Assert.NotNull(_office.Employee.EmailChangedByOfficeAtUtc);
        Assert.Equal("Auth.CredentialAdmin.EmailChangeCooldown", send.Error.Code);
        Assert.InRange(send.Error.RetryAfterSeconds!.Value, 71 * 3600, 72 * 3600);
        Assert.Equal("Auth.CredentialAdmin.EmailChangeCooldown", secure.Error.Code);
        Assert.Equal(CredentialStatus.Active, _office.Employee.CredentialStatus);
        Assert.Empty(_office.Credentials.PasswordResets);
        Assert.NotNull(status.Value.EmailChangeCooldownUntilUtc);
        Assert.False(status.Value.AllowedActions.SendResetLink);
        Assert.False(status.Value.AllowedActions.Secure);
    }

    [Fact]
    public async Task During_the_pause_the_employee_can_still_use_forgot_password()
    {
        await ConfirmOwnerCorrectionAsync();

        // La pausa solo frena lo que dispara la oficina; la cuenta laboral conserva su forgot (IA-47).
        var forgot = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand("employee.new@example.com", UserAccountKind.Staff, _office.Tenant.Id),
            _office.Users,
            _office.Tenants,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            new NoUserLoginLock(),
            new PermissiveLoginThrottler(),
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            _office.Options,
            CancellationToken.None
        );

        Assert.True(forgot.IsSuccess);
        Assert.Single(_office.Credentials.PasswordResets);
    }
}
