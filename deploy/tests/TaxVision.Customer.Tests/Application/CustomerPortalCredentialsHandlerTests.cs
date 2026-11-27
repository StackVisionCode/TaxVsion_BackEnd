using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalCredentials;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// Link de reset del portal desde la ficha (03 §14.2): ABAC como portal-mfa y el step-up viaja a Auth en el sobre.
public sealed class CustomerPortalCredentialsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime ReauthAt = DateTime.UtcNow.AddMinutes(-1);

    private readonly DomainCustomer _customer = NewCustomer();
    private readonly FakePortalCredentials _auth = new();

    private CustomerCommandEventCountTests.AllCustomers Customers => new([_customer]);

    [Fact]
    public async Task An_unassigned_preparer_gets_not_found_and_auth_is_never_called()
    {
        var outsider = Guid.NewGuid();

        var status = await GetCustomerPortalCredentialsHandler.Handle(
            new GetCustomerPortalCredentialsQuery(TenantId, _customer.Id, outsider, CanViewAll: false),
            Customers,
            _auth,
            CancellationToken.None
        );
        var send = await SendCustomerPortalResetLinkHandler.Handle(
            new SendCustomerPortalResetLinkCommand(TenantId, _customer.Id, outsider, false, ReauthAt, "1.2.3.4", "UA"),
            Customers,
            _auth,
            CancellationToken.None
        );
        var cancel = await CancelCustomerPortalResetLinkHandler.Handle(
            new CancelCustomerPortalResetLinkCommand(
                TenantId,
                _customer.Id,
                outsider,
                false,
                ReauthAt,
                "1.2.3.4",
                "UA"
            ),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.Equal("Customer.NotFound", status.Error.Code);
        Assert.Equal("Customer.NotFound", send.Error.Code);
        Assert.Equal("Customer.NotFound", cancel.Error.Code);
        Assert.Equal(0, _auth.Calls);
    }

    [Fact]
    public async Task The_office_admin_sends_and_the_step_up_travels_in_the_envelope()
    {
        var result = await SendCustomerPortalResetLinkHandler.Handle(
            new SendCustomerPortalResetLinkCommand(
                TenantId,
                _customer.Id,
                Admin,
                true,
                ReauthAt,
                "203.0.113.7",
                "Edge"
            ),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.Equal("Issued", result.Value.Outcome);
        Assert.Equal(new PortalCredentialRequest(Admin, ReauthAt, "203.0.113.7", "Edge"), _auth.LastRequest);
        Assert.Equal((TenantId, _customer.Id), _auth.LastTarget);
    }

    // F4-L9: cambio obligatorio y cuenta asegurada siguen el mismo patrón; el motivo viaja a Auth en el sobre.
    [Fact]
    public async Task The_office_admin_requires_a_change_and_the_reason_travels_in_the_envelope()
    {
        var result = await RequireCustomerPortalPasswordChangeHandler.Handle(
            new RequireCustomerPortalPasswordChangeCommand(
                TenantId,
                _customer.Id,
                Admin,
                true,
                ReauthAt,
                "203.0.113.7",
                "Edge",
                "SharedPassword",
                "note",
                null
            ),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.Equal("ChangeRequired", result.Value.CredentialStatus);
        Assert.Equal(
            new PortalCredentialRequest(Admin, ReauthAt, "203.0.113.7", "Edge", "SharedPassword", "note"),
            _auth.LastRequest
        );
    }

    [Fact]
    public async Task Securing_an_unassigned_client_is_not_found_and_auth_is_never_called()
    {
        var result = await SecureCustomerPortalAccountHandler.Handle(
            new SecureCustomerPortalAccountCommand(
                TenantId,
                _customer.Id,
                Guid.NewGuid(),
                false,
                ReauthAt,
                null,
                null,
                "SuspectedCompromise",
                null,
                true
            ),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.Equal("Customer.NotFound", result.Error.Code);
        Assert.Equal(0, _auth.Calls);
    }

    [Fact]
    public async Task Securing_forwards_the_identity_confirmation()
    {
        var result = await SecureCustomerPortalAccountHandler.Handle(
            new SecureCustomerPortalAccountCommand(
                TenantId,
                _customer.Id,
                Admin,
                true,
                ReauthAt,
                null,
                null,
                "SuspectedCompromise",
                null,
                true
            ),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(_auth.LastRequest!.IdentityConfirmed);
    }

    [Fact]
    public async Task Auths_rejection_reaches_the_crm_unchanged()
    {
        var rejection = new Error("Auth.NoPortalAccount", "This client doesn't have portal access yet.");
        _auth.CancelResult = Result.Failure(rejection);

        var result = await CancelCustomerPortalResetLinkHandler.Handle(
            new CancelCustomerPortalResetLinkCommand(TenantId, _customer.Id, Admin, true, ReauthAt, null, null),
            Customers,
            _auth,
            CancellationToken.None
        );

        Assert.Equal(rejection, result.Error);
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                TenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ana", "Client").Value,
                null,
                EmailAddress.Create("ana@example.com").Value,
                null,
                Language.En,
                Admin
            )
            .Value;

    private sealed class FakePortalCredentials : ICustomerPortalCredentialsClient
    {
        public int Calls { get; private set; }
        public PortalCredentialRequest? LastRequest { get; private set; }
        public (Guid TenantId, Guid CustomerId)? LastTarget { get; private set; }
        public Result CancelResult { get; set; } = Result.Success();

        public Task<Result<PortalCredentialStatusResponse>> GetStatusAsync(
            Guid tenantId,
            Guid customerId,
            Guid requestedByUserId,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(
                Result.Success(
                    new PortalCredentialStatusResponse(
                        customerId,
                        false,
                        "Active",
                        null,
                        null,
                        null,
                        false,
                        false,
                        null,
                        new PortalCredentialActions(false, false, false, false)
                    )
                )
            );
        }

        public Task<Result<ResetLinkIssuedResponse>> SendResetLinkAsync(
            Guid tenantId,
            Guid customerId,
            PortalCredentialRequest request,
            CancellationToken ct = default
        )
        {
            Calls++;
            (LastTarget, LastRequest) = ((tenantId, customerId), request);
            return Task.FromResult(
                Result.Success(new ResetLinkIssuedResponse("Issued", Guid.NewGuid(), DateTime.UtcNow.AddHours(24)))
            );
        }

        public Task<Result> CancelResetLinkAsync(
            Guid tenantId,
            Guid customerId,
            PortalCredentialRequest request,
            CancellationToken ct = default
        )
        {
            Calls++;
            (LastTarget, LastRequest) = ((tenantId, customerId), request);
            return Task.FromResult(CancelResult);
        }

        public Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
            Guid tenantId,
            Guid customerId,
            PortalCredentialRequest request,
            CancellationToken ct = default
        )
        {
            Calls++;
            (LastTarget, LastRequest) = ((tenantId, customerId), request);
            return Task.FromResult(Result.Success(new CredentialStateChangedResponse("ChangeRequired", false, 1)));
        }

        public Task<Result<AccountSecuredResponse>> SecureAsync(
            Guid tenantId,
            Guid customerId,
            PortalCredentialRequest request,
            CancellationToken ct = default
        )
        {
            Calls++;
            (LastTarget, LastRequest) = ((tenantId, customerId), request);
            return Task.FromResult(Result.Success(new AccountSecuredResponse(1, 0, 0, Guid.NewGuid())));
        }
    }
}
