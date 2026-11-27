using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

// La oficina ve y resetea el segundo factor del portal de un cliente (vía Customer, M2M; D-10, D-16).
public sealed class CustomerPortalMfaHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private PortalCredentialCommandEnvelope Envelope(
        Guid? requestedBy = null,
        DateTime? reauthenticatedAt = null,
        CredentialAdministrationReasonCode? code = CredentialAdministrationReasonCode.UserRequest
    ) =>
        new(
            requestedBy ?? _office.Owner.Id,
            reauthenticatedAt ?? DateTime.UtcNow.AddMinutes(-1),
            "203.0.113.7",
            "Chrome",
            code,
            "lost phone",
            true
        );

    private Task<Result<ResetCustomerPortalMfaResult>> ResetAsync(
        PortalCredentialCommandEnvelope envelope,
        Guid? customerId = null
    ) =>
        ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(_office.Tenant.Id, customerId ?? _office.CustomerId, envelope, "corr-1"),
            _office.Users,
            new NoUserLoginLock(),
            _office.Cutoff,
            _office.Mfa,
            _office.Options,
            _office.Audit,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    [Fact]
    public async Task Reset_clears_factors_cuts_portal_sessions_and_audits_the_real_ip_and_identity_check()
    {
        _office.Mfa.Enroll(_office.PortalClient);
        var session = TaxVision.Auth.Domain.Sessions.UserSession.Start(
            _office.Tenant.Id,
            _office.PortalClient.Id,
            "Chrome",
            null,
            null
        );
        await _office.Sessions.AddSessionAsync(session);

        var result = await ResetAsync(Envelope());

        Assert.Equal(CustomerPortalMfaResetOutcome.Reset, result.Value.Outcome);
        Assert.False(_office.PortalClient.MfaEnabled);
        Assert.Empty(_office.Mfa.Methods);
        Assert.Empty(_office.Mfa.Codes);
        Assert.All(_office.Mfa.Devices, device => Assert.False(device.IsActive));
        Assert.Equal((session.Id, "mfa_reset_by_office"), Assert.Single(_office.Revocations.Published));

        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.mfa.reset_by_office", log.Action);
        Assert.Equal(_office.Owner.Id, log.UserId);
        Assert.Equal(_office.PortalClient.Id, log.TargetId);
        Assert.Equal("203.0.113.7", log.IpAddress);
        Assert.Equal("Chrome", log.UserAgent);
        Assert.Contains("\"identityConfirmed\":true", log.DetailsJson);
        Assert.DoesNotContain("lost phone", log.DetailsJson);

        var alert = Assert.Single(_office.Bus.Published.OfType<SecurityAlertIntegrationEvent>());
        Assert.Equal(_office.PortalClient.Id, alert.UserId);
        Assert.Equal(SecurityAlertType.MfaResetByOffice, alert.AlertType);
        Assert.Equal("corr-1", alert.CorrelationId);
    }

    [Fact]
    public async Task A_stale_step_up_is_rejected_and_audited()
    {
        _office.Mfa.Enroll(_office.PortalClient);

        var result = await ResetAsync(Envelope(reauthenticatedAt: DateTime.UtcNow.AddMinutes(-30)));

        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", result.Error.Code);
        Assert.True(_office.PortalClient.MfaEnabled);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
    }

    [Fact]
    public async Task Without_a_reason_nothing_changes()
    {
        _office.Mfa.Enroll(_office.PortalClient);

        var result = await ResetAsync(Envelope(code: null));

        Assert.Equal("Auth.CredentialAdmin.ReasonRequired", result.Error.Code);
        Assert.True(_office.PortalClient.MfaEnabled);
    }

    [Fact]
    public async Task An_employee_can_no_longer_reset_a_clients_two_step()
    {
        _office.Mfa.Enroll(_office.PortalClient);

        var result = await ResetAsync(Envelope(requestedBy: _office.Employee.Id));

        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", result.Error.Code);
        Assert.True(_office.PortalClient.MfaEnabled);
    }

    [Fact]
    public async Task Reset_of_a_client_without_two_step_changes_nothing()
    {
        var result = await ResetAsync(Envelope());

        Assert.Equal(CustomerPortalMfaResetOutcome.NotEnabled, result.Value.Outcome);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task Reset_of_a_client_without_portal_access_is_rejected()
    {
        var result = await ResetAsync(Envelope(), customerId: Guid.NewGuid());

        Assert.Equal(ResetCustomerPortalMfaHandler.NoPortalAccount, result.Error);
    }

    [Fact]
    public async Task Status_says_whether_the_client_has_portal_access_two_step_on_and_if_the_office_requires_it()
    {
        var world = new AccountSessionFixture();
        var customerId = Guid.NewGuid();
        var portal = Portal(world, customerId);
        world.Users.Add(portal);
        var mfa = new InMemoryMfaRepository();
        mfa.Enroll(portal);
        mfa.Policy = TenantMfaPolicy.CreateDefault(world.Tenant.Id);
        mfa.Policy.Update(requireForEmployees: false, requireForCustomerPortal: true, trustedDeviceDays: 30);

        var withAccess = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(world.Tenant.Id, customerId),
            world.Users,
            mfa,
            CancellationToken.None
        );
        var withoutAccess = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(world.Tenant.Id, Guid.NewGuid()),
            world.Users,
            mfa,
            CancellationToken.None
        );

        Assert.Equal(new CustomerPortalMfaStatus(true, true, true), withAccess.Value);
        Assert.Equal(new CustomerPortalMfaStatus(false, false, false), withoutAccess.Value);
    }

    private static User Portal(AccountSessionFixture world, Guid customerId) =>
        User.Register(
            world.Tenant.Id,
            "Ana",
            "Client",
            "ana@example.com",
            "hash",
            UserActorType.CustomerPortal,
            customerId
        ).Value;
}
