using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary><i>Reset two-step verification</i> de un empleado por la oficina (03 §3 fila 4, §11).</summary>
public sealed class ResetStaffMfaHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result<StaffMfaResetResponse>> ResetAsync(
        User actor,
        User target,
        CredentialAdministrationReasonCode code = CredentialAdministrationReasonCode.UserRequest,
        string? note = "lost phone",
        bool? identityConfirmed = true
    ) =>
        ResetStaffMfaHandler.Handle(
            new ResetStaffMfaCommand(_office.Tenant.Id, actor.Id, target.Id, code, note, identityConfirmed),
            _office.Users,
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
    public async Task Without_factors_nothing_changes_and_the_outcome_is_not_enabled()
    {
        var result = await ResetAsync(_office.Owner, _office.Employee);

        Assert.Equal(new StaffMfaResetResponse("NotEnabled", 0), result.Value);
        Assert.Empty(_office.Audit.Logs);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task With_factors_it_clears_them_cuts_sessions_and_devices_audits_and_alerts()
    {
        _office.Mfa.Enroll(_office.Employee);
        var sessionId = await StartSessionAsync(_office.Employee);

        var result = await ResetAsync(_office.Owner, _office.Employee);

        Assert.Equal(new StaffMfaResetResponse("Reset", 1), result.Value);
        Assert.False(_office.Employee.MfaEnabled);
        Assert.Empty(_office.Mfa.Methods);
        Assert.Empty(_office.Mfa.Codes);
        Assert.All(_office.Mfa.Devices, device => Assert.False(device.IsActive));
        Assert.Equal((sessionId, "mfa_reset_by_office"), Assert.Single(_office.Revocations.Published));
        Assert.Contains(sessionId, _office.Denylist.Denied);

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.mfa.reset_by_office", log.Action);
        Assert.Equal(_office.Owner.Id, log.UserId);
        Assert.Equal(_office.Employee.Id, log.TargetId);
        Assert.Contains("\"identityConfirmed\":true", log.DetailsJson);
        Assert.Contains("\"reasonCode\":\"UserRequest\"", log.DetailsJson);
        Assert.DoesNotContain("lost phone", log.DetailsJson);

        var alert = Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>());
        Assert.Equal(SecurityAlertType.MfaResetByOffice, alert.AlertType);
        Assert.Equal(_office.Employee.Id, alert.UserId);
        Assert.Single(_office.Bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    [Fact]
    public async Task The_owner_is_protected()
    {
        _office.Settings.OwnerOnly = false;
        _office.Mfa.Enroll(_office.Owner);

        var result = await ResetAsync(_office.CoAdmin, _office.Owner);

        Assert.Equal("Auth.CredentialAdmin.TargetProtected", result.Error.Code);
        Assert.True(_office.Owner.MfaEnabled);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
    }

    [Fact]
    public async Task Other_without_a_note_is_rejected_before_touching_factors()
    {
        _office.Mfa.Enroll(_office.Employee);

        var result = await ResetAsync(
            _office.Owner,
            _office.Employee,
            CredentialAdministrationReasonCode.Other,
            note: null
        );

        Assert.Equal("Auth.CredentialAdmin.ReasonRequired", result.Error.Code);
        Assert.True(_office.Employee.MfaEnabled);
    }

    [Fact]
    public async Task The_panel_offers_the_reset_only_when_the_policy_allows_and_two_step_is_on()
    {
        var off = await StatusAsync(_office.Owner, _office.Employee);
        _office.Mfa.Enroll(_office.Employee);
        var on = await StatusAsync(_office.Owner, _office.Employee);

        Assert.False(off.Value.AllowedActions.ResetMfa);
        Assert.True(on.Value.AllowedActions.ResetMfa);
    }

    private Task<Result<StaffCredentialStatusResponse>> StatusAsync(User actor, User target) =>
        GetStaffCredentialStatusHandler.Handle(
            new GetStaffCredentialStatusQuery(_office.Tenant.Id, actor.Id, target.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );
}
