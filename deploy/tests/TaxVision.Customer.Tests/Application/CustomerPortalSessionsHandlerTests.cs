using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalSessions;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// Sesiones del portal de un cliente desde la ficha: solo quien ve al cliente; la ubicación, con su permiso.
public sealed class CustomerPortalSessionsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task A_preparer_who_is_not_assigned_gets_not_found_and_auth_is_never_called()
    {
        var customer = NewCustomer();
        var auth = new FakeSessions();

        var list = await GetCustomerPortalSessionsHandler.Handle(
            new GetCustomerPortalSessionsQuery(
                TenantId,
                customer.Id,
                Guid.NewGuid(),
                CanViewAll: false,
                CanSeeLocation: true
            ),
            new SingleCustomer(customer),
            auth,
            CancellationToken.None
        );
        var one = await RevokeCustomerPortalSessionHandler.Handle(
            new RevokeCustomerPortalSessionCommand(
                TenantId,
                customer.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                CanViewAll: false
            ),
            new SingleCustomer(customer),
            auth,
            CancellationToken.None
        );
        var all = await RevokeCustomerPortalSessionsHandler.Handle(
            new RevokeCustomerPortalSessionsCommand(
                TenantId,
                customer.Id,
                Guid.NewGuid(),
                CanViewAll: false,
                ForgetTrustedDevices: true
            ),
            new SingleCustomer(customer),
            auth,
            CancellationToken.None
        );

        Assert.Equal("Customer.NotFound", list.Error.Code);
        Assert.Equal("Customer.NotFound", one.Error.Code);
        Assert.Equal("Customer.NotFound", all.Error.Code);
        Assert.Equal(0, auth.Calls);
    }

    [Fact]
    public async Task Without_the_location_permission_the_location_is_hidden()
    {
        var customer = NewCustomer();
        var preparer = Guid.NewGuid();
        customer.GrantAccess(preparer, Admin);

        var result = await GetCustomerPortalSessionsHandler.Handle(
            new GetCustomerPortalSessionsQuery(
                TenantId,
                customer.Id,
                preparer,
                CanViewAll: false,
                CanSeeLocation: false
            ),
            new SingleCustomer(customer),
            new FakeSessions(),
            CancellationToken.None
        );

        var row = Assert.Single(result.Value.Items);
        Assert.Null(row.Location);
        Assert.True(row.LocationHidden);
    }

    [Fact]
    public async Task With_the_location_permission_the_location_stays()
    {
        var customer = NewCustomer();

        var result = await GetCustomerPortalSessionsHandler.Handle(
            new GetCustomerPortalSessionsQuery(TenantId, customer.Id, Admin, CanViewAll: true, CanSeeLocation: true),
            new SingleCustomer(customer),
            new FakeSessions(),
            CancellationToken.None
        );

        Assert.Equal("US", Assert.Single(result.Value.Items).Location!.CountryCode);
    }

    [Fact]
    public async Task Signing_out_passes_who_asked_and_the_trusted_devices_choice_to_auth()
    {
        var customer = NewCustomer();
        var auth = new FakeSessions();
        var sessionId = Guid.NewGuid();

        await RevokeCustomerPortalSessionHandler.Handle(
            new RevokeCustomerPortalSessionCommand(TenantId, customer.Id, sessionId, Admin, CanViewAll: true),
            new SingleCustomer(customer),
            auth,
            CancellationToken.None
        );
        await RevokeCustomerPortalSessionsHandler.Handle(
            new RevokeCustomerPortalSessionsCommand(
                TenantId,
                customer.Id,
                Admin,
                CanViewAll: true,
                ForgetTrustedDevices: true
            ),
            new SingleCustomer(customer),
            auth,
            CancellationToken.None
        );

        Assert.Equal((customer.Id, sessionId, Admin), auth.LastOne);
        Assert.Equal((customer.Id, Admin, true), auth.LastAll);
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                TenantId,
                CustomerKind.Individual,
                PersonalName.Create("Minato", "Kenyimura").Value,
                null,
                EmailAddress.Create("minato@example.com").Value,
                null,
                Language.En,
                Admin
            )
            .Value;

    private sealed class FakeSessions : ICustomerPortalSessionsClient
    {
        public int Calls { get; private set; }
        public (Guid CustomerId, Guid SessionId, Guid RequestedBy)? LastOne { get; private set; }
        public (Guid CustomerId, Guid RequestedBy, bool Forget)? LastAll { get; private set; }

        public Task<Result<PortalSessionPage>> ListSessionsAsync(
            Guid tenantId,
            Guid customerId,
            PortalSessionsQuery query,
            CancellationToken ct = default
        )
        {
            Calls++;
            var session = new PortalSession(
                Guid.NewGuid(),
                "Chrome on Windows",
                "8.8.8.8",
                "agent",
                DateTime.UtcNow,
                DateTime.UtcNow,
                Guid.NewGuid(),
                "Web",
                "Active",
                null,
                null,
                false,
                "8.8.8.8",
                new PortalSessionLocation("US", "California", "Mountain View", 37.42, -122.08, "DB-IP"),
                "Minato Kenyimura",
                "CustomerPortal",
                false
            );
            return Task.FromResult(
                Result.Success(new PortalSessionPage([session], 1, 20, 1, DateTime.UtcNow, DateTime.UtcNow))
            );
        }

        public Task<Result> RevokeSessionAsync(
            Guid tenantId,
            Guid customerId,
            Guid sessionId,
            Guid requestedByUserId,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastOne = (customerId, sessionId, requestedByUserId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> RevokeAllSessionsAsync(
            Guid tenantId,
            Guid customerId,
            Guid requestedByUserId,
            bool forgetTrustedDevices,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastAll = (customerId, requestedByUserId, forgetTrustedDevices);
            return Task.FromResult(Result.Success());
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
