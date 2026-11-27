using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Domain.Credentials;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Link de reset del portal pedido por Customer (M2M, 03 §14.3): Auth vuelve a aplicar la política con quien lo pidió
/// y la ventana de reautenticación; la IP y el agente de la auditoría son los que reenvía Customer.
/// </summary>
public sealed class CustomerPortalPasswordResetLinkHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private PortalCredentialCommandEnvelope Envelope(Guid? by = null, DateTime? reauthAt = null) =>
        new(by ?? _office.Owner.Id, reauthAt ?? DateTime.UtcNow.AddMinutes(-2), "203.0.113.7", "Edge");

    private Task<BuildingBlocks.Results.Result<ResetLinkIssuedResponse>> SendAsync(
        PortalCredentialCommandEnvelope envelope,
        Guid? customerId = null
    ) =>
        SendCustomerPortalPasswordResetLinkHandler.Handle(
            new SendCustomerPortalPasswordResetLinkCommand(
                _office.Tenant.Id,
                customerId ?? _office.CustomerId,
                envelope,
                "corr"
            ),
            _office.Users,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            _office.Tenants,
            new NoUserLoginLock(),
            _office.Options,
            _office.Audit,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    [Fact]
    public async Task The_owner_sends_the_portal_link_audited_with_the_forwarded_ip()
    {
        var result = await SendAsync(Envelope());

        Assert.True(result.IsSuccess);
        var token = Assert.Single(_office.Credentials.PasswordResets);
        Assert.Equal(_office.PortalClient.Id, token.UserId);
        Assert.Equal(ResetOrigin.Administrative, token.Origin);
        var reset = Assert.Single(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal("CustomerPortal", reset.ActorType);
        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.password.reset_sent_by_office", log.Action);
        Assert.Equal(_office.Owner.Id, log.UserId);
        Assert.Equal(_office.PortalClient.Id, log.TargetId);
        Assert.Equal("203.0.113.7", log.IpAddress);
        Assert.Contains("Portal", log.DetailsJson);
    }

    [Fact]
    public async Task A_stale_reauthentication_is_rejected_and_audited()
    {
        var result = await SendAsync(Envelope(reauthAt: DateTime.UtcNow.AddMinutes(-11)));

        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", result.Error.Code);
        Assert.Equal("auth.credential_admin.denied", Assert.Single(_office.Audit.Logs).Action);
        Assert.Empty(_office.Credentials.PasswordResets);
    }

    [Fact]
    public async Task A_client_without_portal_account_answers_no_portal_account() =>
        Assert.Equal("Auth.NoPortalAccount", (await SendAsync(Envelope(), Guid.NewGuid())).Error.Code);

    [Fact]
    public async Task A_co_admin_is_denied_by_the_policy_again() =>
        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", (await SendAsync(Envelope(_office.CoAdmin.Id))).Error.Code);

    [Fact]
    public async Task Cancelling_revokes_the_pending_portal_link()
    {
        var link = _office.SeedLink(_office.PortalClient, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await CancelCustomerPortalPasswordResetLinkHandler.Handle(
            new CancelCustomerPortalPasswordResetLinkCommand(_office.Tenant.Id, _office.CustomerId, Envelope(), "corr"),
            _office.Users,
            _office.Credentials,
            new NoUserLoginLock(),
            _office.Options,
            _office.Audit,
            _office.UnitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PasswordResetTokenStatus.Cancelled, link.StatusAt(DateTime.UtcNow));
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task The_status_of_a_client_without_portal_says_so()
    {
        var result = await GetCustomerPortalCredentialStatusHandler.Handle(
            new GetCustomerPortalCredentialStatusQuery(_office.Tenant.Id, Guid.NewGuid(), _office.Owner.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasPortalAccount);
        Assert.False(result.Value.AllowedActions.SendResetLink);
    }

    [Fact]
    public async Task The_status_of_a_portal_account_shows_the_latest_link()
    {
        _office.SeedLink(_office.PortalClient, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await GetCustomerPortalCredentialStatusHandler.Handle(
            new GetCustomerPortalCredentialStatusQuery(_office.Tenant.Id, _office.CustomerId, _office.Owner.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

        Assert.True(result.Value.HasPortalAccount);
        Assert.Equal(_office.CustomerId, result.Value.CustomerId);
        Assert.Equal(ResetLinkStatus.Pending, result.Value.LatestResetLink!.Status);
        Assert.True(result.Value.AllowedActions.SendResetLink);
    }
}
