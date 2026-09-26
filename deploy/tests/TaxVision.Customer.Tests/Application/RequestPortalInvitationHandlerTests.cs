using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class RequestPortalInvitationHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Asks_auth_with_the_primary_email_and_returns_the_real_outcome()
    {
        var customer = NewCustomer();
        var portal = new FakePortalAccess(
            Result.Success(new PortalInvitationOutcome("Resent", "ana@example.com", DateTime.UtcNow))
        );

        var result = await HandleAsync(customer, portal);

        Assert.Equal("Resent", result.Value.Status);
        Assert.Equal((TenantId, customer.Id, "ana@example.com", Admin), portal.LastCall);
    }

    [Fact]
    public async Task A_rejection_from_auth_reaches_the_crm_with_its_reason()
    {
        var rejection = new Error(
            "Auth.PortalEmailInUse",
            "This email already has client portal access for another client."
        );
        var portal = new FakePortalAccess(Result.Failure<PortalInvitationOutcome>(rejection));

        var result = await HandleAsync(NewCustomer(), portal);

        Assert.Equal(rejection, result.Error);
    }

    [Fact]
    public async Task An_archived_client_is_refused_without_calling_auth()
    {
        var customer = NewCustomer();
        customer.Archive(Admin);
        var portal = new FakePortalAccess(
            Result.Success(new PortalInvitationOutcome("Invited", "ana@example.com", null))
        );

        var result = await HandleAsync(customer, portal);

        Assert.Equal("Customer.Archived", result.Error.Code);
        Assert.Null(portal.LastCall);
    }

    private static Task<Result<RequestPortalInvitationResponse>> HandleAsync(
        DomainCustomer customer,
        FakePortalAccess portal
    ) =>
        RequestPortalInvitationHandler.Handle(
            new RequestPortalInvitationCommand(TenantId, customer.Id, Admin),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );

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
                PreferredChannel.Email,
                Admin
            )
            .Value;

    private sealed class FakePortalAccess(Result<PortalInvitationOutcome> outcome) : ICustomerPortalAccessClient
    {
        public (Guid TenantId, Guid CustomerId, string Email, Guid RequestedBy)? LastCall { get; private set; }

        public Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
            Guid tenantId,
            Guid customerId,
            string email,
            Guid requestedByUserId,
            CancellationToken ct = default
        )
        {
            LastCall = (tenantId, customerId, email, requestedByUserId);
            return Task.FromResult(outcome);
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
