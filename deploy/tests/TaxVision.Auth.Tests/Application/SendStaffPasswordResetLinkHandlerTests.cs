using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Envío del link de reset de un empleado por la oficina (03 §3 fila 1, §14.1).</summary>
public sealed class SendStaffPasswordResetLinkHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result<ResetLinkIssuedResponse>> SendAsync(User actor, User target, Guid? tenantId = null) =>
        SendStaffPasswordResetLinkHandler.Handle(
            new SendStaffPasswordResetLinkCommand(tenantId ?? _office.Tenant.Id, actor.Id, target.Id),
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

    [Fact]
    public async Task The_owner_sends_an_administrative_link_with_alert_and_audit()
    {
        var result = await SendAsync(_office.Owner, _office.Employee);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResetLinkIssueOutcome.Issued, result.Value.Outcome);
        var token = Assert.Single(_office.Credentials.PasswordResets);
        Assert.Equal(result.Value.ResetTokenId, token.Id);
        Assert.Equal(ResetOrigin.Administrative, token.Origin);

        var reset = Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal("Administrative", reset.Origin);
        Assert.Null(reset.RawToken);
        var alert = Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>());
        Assert.Equal(SecurityAlertType.PasswordResetSentByOffice, alert.AlertType);
        Assert.Equal(_office.Employee.Id, alert.UserId);
        // Notification no avisa al actor de su propia acción.
        Assert.Contains($"\"actorUserId\":\"{_office.Owner.Id}\"", alert.DetailsJson);

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.password.reset_sent_by_office", log.Action);
        Assert.Equal(_office.Owner.Id, log.UserId);
        Assert.Equal(_office.Employee.Id, log.TargetId);
        Assert.True(log.Success);
        Assert.Equal(1, _office.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_co_admin_is_denied_with_owner_only_and_the_denial_is_audited()
    {
        var result = await SendAsync(_office.CoAdmin, _office.Employee);

        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", result.Error.Code);
        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.credential_admin.denied", log.Action);
        Assert.False(log.Success);
        Assert.Contains("Auth.CredentialAdmin.OwnerOnly", log.DetailsJson);
        Assert.Empty(_office.Bus.Published);
        Assert.Empty(_office.Credentials.PasswordResets);
    }

    [Fact]
    public async Task Resending_supersedes_the_pending_link()
    {
        var old = _office.SeedLink(_office.Employee);

        var result = await SendAsync(_office.Owner, _office.Employee);

        Assert.True(result.IsSuccess);
        Assert.Equal(PasswordResetTokenStatus.Superseded, old.StatusAt(DateTime.UtcNow));
        Assert.Single(
            _office.Credentials.PasswordResets,
            t => t.StatusAt(DateTime.UtcNow) == PasswordResetTokenStatus.Pending
        );
    }

    [Fact]
    public async Task A_target_of_another_office_is_not_found()
    {
        var stranger = _office.Add(UserActorType.TenantEmployee, tenantId: Guid.NewGuid());

        var result = await SendAsync(_office.Owner, stranger);

        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task The_daily_cap_per_target_answers_429_and_is_audited()
    {
        for (var i = 0; i < _office.Settings.TargetDailyCap; i++)
            _office.SeedLink(_office.Employee, ResetOrigin.Administrative, Guid.NewGuid());

        var result = await SendAsync(_office.Owner, _office.Employee);

        Assert.Equal("Auth.CredentialAdmin.TargetRateLimited", result.Error.Code);
        // El CRM deshabilita el botón con este tiempo (cota: cuando sale de la ventana el último link de la oficina).
        Assert.InRange(result.Error.RetryAfterSeconds ?? 0, 86_000, 86_400);
        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal(AuthAuditAction.CredentialAdminDenied, log.Action);
    }

    [Fact]
    public async Task A_repeat_returns_the_pending_link_without_a_second_email()
    {
        await SendAsync(_office.Owner, _office.Employee);
        var again = await SendAsync(_office.Owner, _office.Employee);

        Assert.Equal(ResetLinkIssueOutcome.AlreadyPending, again.Value.Outcome);
        Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
    }
}
