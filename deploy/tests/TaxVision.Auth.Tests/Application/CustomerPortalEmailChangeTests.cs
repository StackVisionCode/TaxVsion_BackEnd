using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Application.Customers.IntegrationEvents;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Cambio de correo de un cliente con portal hecho por la oficina (D-08): el login no cambia hasta que el cliente
/// verifica el nuevo; aviso al anterior; 72 h sin reset administrativo tras el cambio.
/// </summary>
public sealed class CustomerPortalEmailChangeTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task OfficeChangesContactEmailAsync(string? email, Guid? modifiedBy = null, string? previousEmail = null) =>
        CustomerEmailChangedByOfficeConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = _office.Tenant.Id,
                CustomerId = _office.CustomerId,
                DisplayName = "Ana Client",
                PrimaryEmail = email,
                PreviousEmail = previousEmail,
                Language = "En",
                ModifiedByUserId = modifiedBy ?? _office.Owner.Id,
            },
            _office.Users,
            _office.Credentials,
            _office.Tokens,
            _office.Protector,
            _office.Tenants,
            _office.Options,
            _office.Audit,
            _office.Correlation,
            _office.UnitOfWork,
            _office.Bus,
            CancellationToken.None
        );

    private Task<Result> ConfirmAsync(string rawToken) =>
        ConfirmEmailChangeHandler.Handle(
            new ConfirmEmailChangeCommand(rawToken),
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

    private Task<Result<ResetLinkIssuedResponse>> SendResetLinkAsync() =>
        SendCustomerPortalPasswordResetLinkHandler.Handle(
            new SendCustomerPortalPasswordResetLinkCommand(
                _office.Tenant.Id,
                _office.CustomerId,
                new PortalCredentialCommandEnvelope(_office.Owner.Id, DateTime.UtcNow.AddMinutes(-1), "1.2.3.4", "UA"),
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
    public async Task The_new_address_gets_a_link_the_old_one_a_notice_and_the_login_stays_the_same()
    {
        var previous = _office.PortalClient.Email;

        await OfficeChangesContactEmailAsync("Ana.New@Example.com");

        Assert.Equal(previous, _office.PortalClient.Email);
        var verification = Assert.Single(_office.Credentials.EmailVerifications);
        Assert.Equal("ana.new@example.com", verification.NewEmail);
        Assert.Equal(_office.Owner.Id, verification.RequestedByUserId);
        var requested = Assert.Single(_office.Bus.Published.OfType<EmailChangeRequestedIntegrationEvent>());
        Assert.Equal(previous, requested.CurrentEmail);
        Assert.Equal("ana.new@example.com", requested.NewEmail);
        Assert.Equal("CustomerPortal", requested.ActorType);
        Assert.Equal("auth.email.change_requested_by_office", Assert.Single(_office.Audit.Logs).Action);
    }

    [Fact]
    public async Task An_employee_editing_the_contact_email_does_not_touch_the_login()
    {
        // Solo el Owner puede mover el correo de login (03 §21): la edición de un empleado queda en la ficha.
        await OfficeChangesContactEmailAsync("attacker@example.com", modifiedBy: _office.Employee.Id);

        Assert.Empty(_office.Credentials.EmailVerifications);
        Assert.Empty(_office.Bus.Published.OfType<EmailChangeRequestedIntegrationEvent>());
        Assert.Empty(_office.Audit.Logs);
    }

    [Fact]
    public async Task While_paused_the_new_address_cannot_reset_the_password_on_its_own()
    {
        _office.Credentials.EmailVerifications.Add(
            EmailVerificationToken.Create(
                _office.Tenant.Id,
                _office.PortalClient.Id,
                "ana.new@example.com",
                _office.Tokens.Hash("raw-confirm"),
                TimeSpan.FromHours(24),
                requestedByUserId: _office.Owner.Id
            )
        );
        await ConfirmAsync("raw-confirm");

        var forgot = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand("ana.new@example.com", UserAccountKind.Portal, _office.Tenant.Id),
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
        Assert.Empty(_office.Credentials.PasswordResets);
        Assert.Empty(_office.Bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Later_updates_with_the_same_address_do_not_send_another_link()
    {
        await OfficeChangesContactEmailAsync("ana.new@example.com");

        await OfficeChangesContactEmailAsync("ana.new@example.com");

        Assert.Single(_office.Credentials.EmailVerifications);
        Assert.Single(_office.Bus.Published.OfType<EmailChangeRequestedIntegrationEvent>());
    }

    // SR-09: si la oficina guarda a propósito un contacto distinto del login, editar otra cosa no pide verificación.
    [Fact]
    public async Task An_edit_that_did_not_change_the_contact_email_does_not_touch_the_login()
    {
        await OfficeChangesContactEmailAsync("spouse@example.com", previousEmail: "Spouse@Example.com");

        Assert.Empty(_office.Credentials.EmailVerifications);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task An_edit_that_changed_the_contact_email_asks_for_verification()
    {
        await OfficeChangesContactEmailAsync("ana.new@example.com", previousEmail: "spouse@example.com");

        Assert.Equal("ana.new@example.com", Assert.Single(_office.Credentials.EmailVerifications).NewEmail);
    }

    [Fact]
    public async Task Changes_made_by_the_client_or_matching_the_login_are_ignored()
    {
        await OfficeChangesContactEmailAsync("ana.self@example.com", modifiedBy: _office.PortalClient.Id);
        await OfficeChangesContactEmailAsync(_office.PortalClient.Email.Value.ToUpperInvariant());
        await OfficeChangesContactEmailAsync(null);

        Assert.Empty(_office.Credentials.EmailVerifications);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task After_the_client_confirms_an_office_change_reset_links_pause_for_72_hours()
    {
        _office.Credentials.EmailVerifications.Add(
            EmailVerificationToken.Create(
                _office.Tenant.Id,
                _office.PortalClient.Id,
                "ana.new@example.com",
                _office.Tokens.Hash("raw-confirm"),
                TimeSpan.FromHours(24),
                requestedByUserId: _office.Owner.Id
            )
        );

        var confirmed = await ConfirmAsync("raw-confirm");
        var send = await SendResetLinkAsync();
        var status = await GetCustomerPortalCredentialStatusHandler.Handle(
            new GetCustomerPortalCredentialStatusQuery(_office.Tenant.Id, _office.CustomerId, _office.Owner.Id),
            _office.Users,
            _office.Credentials,
            _office.Options,
            _office.Deliveries,
            CancellationToken.None
        );

        Assert.True(confirmed.IsSuccess);
        Assert.Equal("ana.new@example.com", _office.PortalClient.Email.Value);
        Assert.NotNull(_office.PortalClient.EmailChangedByOfficeAtUtc);
        Assert.Equal("Auth.CredentialAdmin.EmailChangeCooldown", send.Error.Code);
        Assert.InRange(send.Error.RetryAfterSeconds!.Value, 71 * 3600, 72 * 3600);
        Assert.Empty(_office.Credentials.PasswordResets);
        Assert.NotNull(status.Value.EmailChangeCooldownUntilUtc);
        Assert.False(status.Value.AllowedActions.SendResetLink);
        Assert.False(status.Value.AllowedActions.Secure);
    }

    [Fact]
    public async Task A_change_confirmed_by_the_client_on_their_own_does_not_pause_reset_links()
    {
        _office.Credentials.EmailVerifications.Add(
            EmailVerificationToken.Create(
                _office.Tenant.Id,
                _office.PortalClient.Id,
                "ana.self@example.com",
                _office.Tokens.Hash("raw-self"),
                TimeSpan.FromHours(1)
            )
        );

        await ConfirmAsync("raw-self");
        var send = await SendResetLinkAsync();

        Assert.Null(_office.PortalClient.EmailChangedByOfficeAtUtc);
        Assert.True(send.IsSuccess);
    }
}
