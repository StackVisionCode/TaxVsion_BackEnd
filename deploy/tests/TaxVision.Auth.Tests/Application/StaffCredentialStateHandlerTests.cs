using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary><i>Require password change</i> y <i>Secure account</i> de un empleado por la oficina (03 §3 filas 2–3, AJ-15).</summary>
public sealed class StaffCredentialStateHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
        User actor,
        User target,
        CredentialAdministrationReasonCode code = CredentialAdministrationReasonCode.OfficePolicy,
        string? note = "private note"
    ) =>
        RequireStaffPasswordChangeHandler.Handle(
            new RequireStaffPasswordChangeCommand(_office.Tenant.Id, actor.Id, target.Id, code, note),
            _office.Users,
            new NoUserLoginLock(),
            _office.Cutoff,
            _office.Options,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    private Task<Result<AccountSecuredResponse>> SecureAsync(
        User actor,
        User target,
        CredentialAdministrationReasonCode code = CredentialAdministrationReasonCode.SuspectedCompromise,
        string? note = "private note",
        bool? identityConfirmed = null
    ) =>
        SecureStaffAccountHandler.Handle(
            new SecureStaffAccountCommand(_office.Tenant.Id, actor.Id, target.Id, code, note, identityConfirmed),
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

    private async Task<Guid> StartSessionAsync(User user)
    {
        var session = TaxVision.Auth.Domain.Sessions.UserSession.Start(user.TenantId, user.Id, "Chrome", null, null);
        await _office.Sessions.AddSessionAsync(session);
        return session.Id;
    }

    [Fact]
    public async Task Requiring_a_change_marks_the_state_cuts_sessions_audits_and_alerts()
    {
        var sessionId = await StartSessionAsync(_office.Employee);

        var result = await RequireChangeAsync(_office.Owner, _office.Employee);

        Assert.Equal(new CredentialStateChangedResponse(CredentialStatus.ChangeRequired, false, 1), result.Value);
        Assert.Equal(CredentialStatus.ChangeRequired, _office.Employee.CredentialStatus);
        Assert.Equal("private note", _office.Employee.CredentialStateReasonNote);
        Assert.Equal((sessionId, "credential_change_required"), Assert.Single(_office.Revocations.Published));
        Assert.Contains(sessionId, _office.Denylist.Denied);

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.password.change_required", log.Action);
        Assert.Equal(_office.Employee.Id, log.TargetId);
        Assert.Contains("\"reasonCode\":\"OfficePolicy\"", log.DetailsJson);
        Assert.DoesNotContain("private note", log.DetailsJson);

        var alert = Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>());
        Assert.Equal(SecurityAlertType.PasswordChangeRequired, alert.AlertType);
        Assert.Contains($"\"actorUserId\":\"{_office.Owner.Id}\"", alert.DetailsJson);
        Assert.Single(_office.Bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    [Fact]
    public async Task Requiring_a_change_twice_is_idempotent()
    {
        await RequireChangeAsync(_office.Owner, _office.Employee);

        var again = await RequireChangeAsync(_office.Owner, _office.Employee);

        Assert.True(again.Value.AlreadyInState);
        Assert.Single(_office.Audit.Logs);
        Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>());
    }

    [Fact]
    public async Task Requiring_a_change_on_a_secured_account_is_an_invalid_transition()
    {
        _office.Employee.Secure();

        var result = await RequireChangeAsync(_office.Owner, _office.Employee);

        Assert.Equal("Auth.CredentialAdmin.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public async Task Other_without_a_note_is_rejected()
    {
        var result = await RequireChangeAsync(
            _office.Owner,
            _office.Employee,
            CredentialAdministrationReasonCode.Other,
            note: null
        );

        Assert.Equal("Auth.CredentialAdmin.ReasonRequired", result.Error.Code);
        Assert.Equal(CredentialStatus.Active, _office.Employee.CredentialStatus);
    }

    [Fact]
    public async Task A_co_admin_is_denied_and_the_denial_is_audited()
    {
        var result = await SecureAsync(_office.CoAdmin, _office.Employee);

        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", result.Error.Code);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
        Assert.Equal(CredentialStatus.Active, _office.Employee.CredentialStatus);
    }

    [Fact]
    public async Task Securing_cuts_access_forgets_devices_keeps_mfa_and_sends_one_secure_link()
    {
        var sessionId = await StartSessionAsync(_office.Employee);
        _office.Mfa.Enroll(_office.Employee);
        var pending = _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await SecureAsync(_office.Owner, _office.Employee, identityConfirmed: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialStatus.ResetRequired, _office.Employee.CredentialStatus);
        Assert.Equal(1, result.Value.SessionsRevoked);
        Assert.Equal(1, result.Value.TrustedDevicesRevoked);
        Assert.Equal(1, result.Value.LinksRevoked);
        Assert.False(result.Value.AlreadyInState);
        Assert.Contains(sessionId, _office.Denylist.Denied);
        Assert.Equal((sessionId, "account_secured"), Assert.Single(_office.Revocations.Published));
        Assert.All(_office.Mfa.Devices, device => Assert.False(device.IsActive));
        Assert.True(_office.Employee.MfaEnabled);
        Assert.Single(_office.Mfa.Methods);
        Assert.Equal(ResetRevocationReason.AccountSecured, pending.RevocationReason);

        var link = Assert.Single(_office.Credentials.PasswordResets, t => t.Id == result.Value.ResetTokenId);
        Assert.Equal(ResetOrigin.Incident, link.Origin);
        Assert.True(link.IsUsable(DateTime.UtcNow));
        var reset = Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal("Incident", reset.Origin);
        Assert.Equal(
            SecurityAlertType.AccountSecured,
            Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>()).AlertType
        );

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.user.secured", log.Action);
        Assert.Contains("\"identityConfirmed\":true", log.DetailsJson);
        Assert.Contains("\"reasonCode\":\"SuspectedCompromise\"", log.DetailsJson);
        Assert.DoesNotContain("private note", log.DetailsJson);
    }

    // AJ-15: asegurar de nuevo dentro de 60 s es idempotente; después (u otro actor) reemite el link seguro.
    [Fact]
    public async Task Securing_again_within_a_minute_by_the_same_owner_does_not_resend()
    {
        var first = await SecureAsync(_office.Owner, _office.Employee);

        var again = await SecureAsync(_office.Owner, _office.Employee);

        Assert.True(again.Value.AlreadyInState);
        Assert.Equal(first.Value.ResetTokenId, again.Value.ResetTokenId);
        Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Resending_the_secure_link_supersedes_the_pending_one_and_issues_another_incident()
    {
        _office.Settings.OwnerOnly = false;
        var first = await SecureAsync(_office.Owner, _office.Employee);

        var resend = await SecureAsync(
            _office.CoAdmin,
            _office.Employee,
            CredentialAdministrationReasonCode.Other,
            "bounced"
        );

        Assert.True(resend.Value.AlreadyInState);
        Assert.NotEqual(first.Value.ResetTokenId, resend.Value.ResetTokenId);
        var old = Assert.Single(_office.Credentials.PasswordResets, t => t.Id == first.Value.ResetTokenId);
        Assert.Equal(ResetRevocationReason.Superseded, old.RevocationReason);
        Assert.Equal(2, _office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>().Count());
        Assert.Equal("bounced", _office.Employee.CredentialStateReasonNote);
    }

    // 03 §14.1: sobre una cuenta asegurada el link normal no sirve; se usa "secure" de nuevo.
    [Fact]
    public async Task Sending_a_normal_link_to_a_secured_account_is_an_invalid_transition()
    {
        _office.Employee.Secure();

        var result = await SendStaffPasswordResetLinkHandler.Handle(
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

        Assert.Equal("Auth.CredentialAdmin.InvalidTransition", result.Error.Code);
        Assert.Empty(_office.Credentials.PasswordResets);
    }

    [Fact]
    public async Task The_status_shows_the_state_and_which_actions_apply()
    {
        await RequireChangeAsync(_office.Owner, _office.Employee);

        var status = await GetStaffCredentialStatusHandler.Handle(
            new GetStaffCredentialStatusQuery(_office.Tenant.Id, _office.Owner.Id, _office.Employee.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

        Assert.Equal(CredentialStatus.ChangeRequired, status.Value.CredentialStatus);
        Assert.Equal(CredentialAdministrationReasonCode.OfficePolicy, status.Value.ReasonCode);
        Assert.NotNull(status.Value.CredentialStateChangedAtUtc);
        Assert.Equal(new StaffCredentialActions(true, false, true, false), status.Value.AllowedActions);
    }
}
