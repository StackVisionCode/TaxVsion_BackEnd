using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalMfa;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// Segundo factor del portal de un cliente desde la ficha: solo quien ve al cliente puede verlo o resetearlo.
public sealed class CustomerPortalMfaHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task An_assigned_preparer_resets_the_clients_two_step_and_auth_records_who_asked()
    {
        var customer = NewCustomer();
        var preparer = Guid.NewGuid();
        customer.GrantAccess(preparer, Admin);
        var portal = new FakePortalSecurity();

        var result = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(TenantId, customer.Id, preparer, CanViewAll: false),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );

        Assert.Equal("Reset", result.Value.Outcome);
        Assert.Equal((TenantId, customer.Id, preparer), portal.LastReset);
    }

    [Fact]
    public async Task A_preparer_who_is_not_assigned_gets_not_found_and_auth_is_never_called()
    {
        var customer = NewCustomer();
        var portal = new FakePortalSecurity();

        var reset = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(TenantId, customer.Id, Guid.NewGuid(), CanViewAll: false),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );
        var status = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(TenantId, customer.Id, Guid.NewGuid(), CanViewAll: false),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );

        Assert.Equal("Customer.NotFound", reset.Error.Code);
        Assert.Equal("Customer.NotFound", status.Error.Code);
        Assert.Null(portal.LastReset);
        Assert.False(portal.StatusAsked);
    }

    [Fact]
    public async Task With_view_all_the_office_sees_the_status_and_auths_rejection_reaches_the_crm()
    {
        var customer = NewCustomer();
        var rejection = new Error("Auth.NoPortalAccount", "This client doesn't have portal access yet.");
        var portal = new FakePortalSecurity { ResetResult = Result.Failure<PortalMfaResetOutcome>(rejection) };

        var status = await GetCustomerPortalMfaHandler.Handle(
            new GetCustomerPortalMfaQuery(TenantId, customer.Id, Admin, CanViewAll: true),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );
        var reset = await ResetCustomerPortalMfaHandler.Handle(
            new ResetCustomerPortalMfaCommand(TenantId, customer.Id, Admin, CanViewAll: true),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );

        Assert.Equal(new PortalMfaStatus(true, true, false), status.Value);
        Assert.Equal(rejection, reset.Error);
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

    private sealed class FakePortalSecurity : ICustomerPortalAccessClient
    {
        public (Guid TenantId, Guid CustomerId, Guid RequestedBy)? LastReset { get; private set; }
        public bool StatusAsked { get; private set; }
        public Result<PortalMfaResetOutcome> ResetResult { get; init; } =
            Result.Success(new PortalMfaResetOutcome("Reset"));

        public Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
            Guid tenantId,
            Guid customerId,
            string email,
            Guid requestedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<PortalMfaStatus>> GetMfaStatusAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        )
        {
            StatusAsked = true;
            return Task.FromResult(Result.Success(new PortalMfaStatus(true, true, false)));
        }

        public Task<Result<PortalMfaResetOutcome>> ResetMfaAsync(
            Guid tenantId,
            Guid customerId,
            Guid requestedByUserId,
            CancellationToken ct = default
        )
        {
            LastReset = (tenantId, customerId, requestedByUserId);
            return Task.FromResult(ResetResult);
        }
    }

    private sealed class SingleCustomer(DomainCustomer customer) : ICustomerRepository
    {
        public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<DomainCustomer?>(id == customer.Id ? customer : null);

        public Task<IReadOnlyList<DomainCustomer>> ListForAssignmentAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeRelationId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid tenantId,
            Guid preparerUserId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task AddAsync(DomainCustomer customer, CancellationToken ct) => throw new NotImplementedException();
    }
}
