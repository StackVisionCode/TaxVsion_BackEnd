using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Security;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalMfa;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// Segundo factor del portal de un cliente desde la ficha: solo quien ve al cliente puede verlo o resetearlo.
public sealed class CustomerPortalMfaHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime ReauthAt = DateTime.UtcNow.AddMinutes(-2);

    private static ResetCustomerPortalMfaCommand Reset(Guid customerId, Guid actor, bool canViewAll) =>
        new(
            TenantId,
            customerId,
            actor,
            canViewAll,
            ReauthAt,
            "203.0.113.7",
            "Chrome",
            "UserRequest",
            "lost phone",
            true
        );

    [Fact]
    public async Task A_visible_client_is_reset_and_auth_receives_step_up_real_ip_and_reason()
    {
        var customer = NewCustomer();
        var portal = new FakePortalSecurity();

        var result = await ResetCustomerPortalMfaHandler.Handle(
            Reset(customer.Id, Admin, canViewAll: true),
            new SingleCustomer(customer),
            portal,
            CancellationToken.None
        );

        Assert.Equal("Reset", result.Value.Outcome);
        Assert.Equal((TenantId, customer.Id), portal.LastReset);
        Assert.Equal(
            new PortalCredentialRequest(Admin, ReauthAt, "203.0.113.7", "Chrome", "UserRequest", "lost phone", true),
            portal.LastRequest
        );
    }

    [Fact]
    public void The_reset_endpoint_needs_its_own_admin_permission_and_recent_authentication()
    {
        var action = typeof(TaxVision.Customer.Api.Controllers.CustomerController).GetMethod("ResetPortalMfa")!;

        Assert.Equal("customers.portal_mfa.reset", CustomersPermissions.PortalMfaReset);
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + CustomersPermissions.PortalMfaReset,
            action.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.NotNull(action.GetCustomAttribute<RequireRecentAuthenticationAttribute>());
        Assert.Equal(
            new[] { ActorType.TenantAdmin },
            action.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
    }

    [Fact]
    public async Task A_preparer_who_is_not_assigned_gets_not_found_and_auth_is_never_called()
    {
        var customer = NewCustomer();
        var portal = new FakePortalSecurity();

        var reset = await ResetCustomerPortalMfaHandler.Handle(
            Reset(customer.Id, Guid.NewGuid(), canViewAll: false),
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
            Reset(customer.Id, Admin, canViewAll: true),
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
        public (Guid TenantId, Guid CustomerId)? LastReset { get; private set; }
        public PortalCredentialRequest? LastRequest { get; private set; }
        public bool StatusAsked { get; private set; }
        public Result<PortalMfaResetOutcome> ResetResult { get; init; } =
            Result.Success(new PortalMfaResetOutcome("Reset"));

        public Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
            Guid tenantId,
            Guid customerId,
            string email,
            string? firstName,
            string? lastName,
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
            PortalCredentialRequest request,
            CancellationToken ct = default
        )
        {
            LastReset = (tenantId, customerId);
            LastRequest = request;
            return Task.FromResult(ResetResult);
        }
    }

    private sealed class SingleCustomer(DomainCustomer customer) : ICustomerRepository
    {
        public Task<DomainCustomer?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
            Task.FromResult<DomainCustomer?>(id == customer.Id && tenantId == customer.TenantId ? customer : null);

        public Task<DomainCustomer?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(new[] { customer }.FirstOrDefault(c => c.TenantId == tenantId && c.Id == customerId));

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

        public async Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            IReadOnlyCollection<string> blindIndexes,
            Guid? excludeCustomerId,
            CancellationToken ct
        )
        {
            foreach (var blindIndex in blindIndexes)
                if (await FindCustomerIdByFiscalBlindIndexAsync(tenantId, blindIndex, excludeCustomerId, ct) is { } hit)
                    return hit;
            return null;
        }

        public async Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid tenantId,
            IReadOnlyCollection<string> blindIndexes,
            Guid? excludeRelationId,
            CancellationToken ct
        )
        {
            foreach (var blindIndex in blindIndexes)
                if (await FindRelationIdByFiscalBlindIndexAsync(tenantId, blindIndex, excludeRelationId, ct) is { } hit)
                    return hit;
            return null;
        }

        public Task<IReadOnlyList<DomainCustomer>> ListNonPrimaryAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<int> CountActiveByAssignedPreparerAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<DomainCustomer>> ListArchivedAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<OffboardingClientCounts> CountOffboardingImpactAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct
        ) => Task.FromResult(OffboardingClientCounts.None);

        public Task<Guid?> FindIdByOriginAsync(
            Guid tenantId,
            string sourceService,
            Guid sourceReferenceId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);
    }
}
