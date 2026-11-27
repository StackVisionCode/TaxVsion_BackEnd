using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// <i>Require change</i> y <i>Secure</i> del portal de un cliente pedidos por Customer (M2M, 03 §14.3): Auth vuelve a
/// aplicar la política con quien lo pidió y la ventana de reautenticación.
/// </summary>
public sealed class CustomerPortalCredentialStateHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private PortalCredentialCommandEnvelope Envelope(
        CredentialAdministrationReasonCode? code = CredentialAdministrationReasonCode.SharedPassword,
        DateTime? reauthAt = null
    ) => new(_office.Owner.Id, reauthAt ?? DateTime.UtcNow.AddMinutes(-2), "203.0.113.7", "Edge", code, "note");

    private Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
        PortalCredentialCommandEnvelope envelope,
        Guid? customerId = null
    ) =>
        RequireCustomerPortalPasswordChangeHandler.Handle(
            new RequireCustomerPortalPasswordChangeCommand(
                _office.Tenant.Id,
                customerId ?? _office.CustomerId,
                envelope,
                "corr"
            ),
            _office.Users,
            new NoUserLoginLock(),
            _office.Cutoff,
            _office.Options,
            _office.Audit,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    private Task<Result<AccountSecuredResponse>> SecureAsync(PortalCredentialCommandEnvelope envelope) =>
        SecureCustomerPortalAccountHandler.Handle(
            new SecureCustomerPortalAccountCommand(_office.Tenant.Id, _office.CustomerId, envelope, "corr"),
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
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    [Fact]
    public async Task The_owner_requires_a_portal_password_change_audited_with_the_forwarded_ip()
    {
        var result = await RequireChangeAsync(Envelope());

        Assert.Equal(CredentialStatus.ChangeRequired, result.Value.CredentialStatus);
        Assert.Equal(CredentialStatus.ChangeRequired, _office.PortalClient.CredentialStatus);
        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.password.change_required", log.Action);
        Assert.Equal("203.0.113.7", log.IpAddress);
        Assert.Contains("Portal", log.DetailsJson);
    }

    [Fact]
    public async Task The_owner_secures_the_portal_account_with_an_incident_link()
    {
        var result = await SecureAsync(Envelope(CredentialAdministrationReasonCode.SuspectedCompromise));

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialStatus.ResetRequired, _office.PortalClient.CredentialStatus);
        var reset = Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal("Incident", reset.Origin);
        Assert.Equal("CustomerPortal", reset.ActorType);
        Assert.Equal("auth.user.secured", Assert.Single(_office.Audit.Logs).Action);
    }

    [Fact]
    public async Task Without_a_reason_code_nothing_changes()
    {
        var result = await RequireChangeAsync(Envelope(code: null));

        Assert.Equal("Auth.CredentialAdmin.ReasonRequired", result.Error.Code);
        Assert.Equal(CredentialStatus.Active, _office.PortalClient.CredentialStatus);
    }

    [Fact]
    public async Task A_stale_reauthentication_is_rejected_and_audited()
    {
        var result = await SecureAsync(
            Envelope(CredentialAdministrationReasonCode.SuspectedCompromise, DateTime.UtcNow.AddMinutes(-11))
        );

        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", result.Error.Code);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
        Assert.Equal(CredentialStatus.Active, _office.PortalClient.CredentialStatus);
    }

    [Fact]
    public async Task A_customer_without_a_portal_account_answers_no_portal_account()
    {
        var result = await RequireChangeAsync(Envelope(), Guid.NewGuid());

        Assert.Equal("Auth.NoPortalAccount", result.Error.Code);
    }
}
