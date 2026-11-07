using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.BulkAssign;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Employees;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Reparto masivo "automático": si el cliente no tenía responsable, el asignado queda de responsable;
/// si ya tenía, entra como acceso adicional; lo ya asignado se salta (idempotente).
/// </summary>
public sealed class BulkAssignCustomersHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Assignee = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Client_without_a_preparer_makes_the_assignee_the_responsible()
    {
        var c = NewCustomer();
        var bus = new FakeMessageBus();

        var result = await Handle(new FakeCustomers(c), bus, [c.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Assigned);
        Assert.Equal(Assignee, c.AssignedPreparerUserId);
        Assert.Contains(c.Assignments, a => a.UserId == Assignee && a.IsPrimary);
        Assert.Single(bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task Client_with_a_preparer_gets_the_assignee_as_extra_access()
    {
        var c = NewCustomer();
        c.AssignPreparer(Other, Admin); // ya tiene responsable
        var bus = new FakeMessageBus();

        var result = await Handle(new FakeCustomers(c), bus, [c.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Other, c.AssignedPreparerUserId); // el responsable no cambia
        Assert.Contains(c.Assignments, a => a.UserId == Assignee && !a.IsPrimary);
        Assert.Single(bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Already_assigned_client_is_skipped()
    {
        var c = NewCustomer();
        c.GrantAccess(Assignee, Admin);
        var bus = new FakeMessageBus();

        var result = await Handle(new FakeCustomers(c), bus, [c.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Assigned);
        Assert.Equal(1, result.Value.AlreadyAssigned);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Ineligible_assignee_is_rejected()
    {
        var c = NewCustomer();
        var bus = new FakeMessageBus();

        var result = await Handle(new FakeCustomers(c), bus, [c.Id], eligible: false);

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.AssigneeNotEligible", result.Error.Code);
    }

    // Lo sembrado ya está guardado; lo emitido por el comando sale por el bus como en Wolverine.
    private static async Task<Result<BulkAssignResponse>> Handle(
        FakeCustomers repo,
        FakeMessageBus bus,
        IReadOnlyList<Guid> ids,
        bool eligible = true
    )
    {
        foreach (var customer in repo.All)
            customer.ClearDomainEvents();
        var result = await BulkAssignCustomersHandler.Handle(
            new BulkAssignCustomersCommand(TenantId, Assignee, ids, Admin),
            repo,
            new FakeDirectory(eligible),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, repo, repo.All);
        return result;
    }

    private static DomainCustomer NewCustomer()
    {
        var name = PersonalName.Create("Test", "Client").Value;
        var email = EmailAddress.Create($"c-{Guid.NewGuid():N}@example.com").Value;
        return DomainCustomer
            .Register(TenantId, CustomerKind.Individual, name, null, email, null, Language.En, Admin)
            .Value;
    }

    private sealed class FakeDirectory(bool eligible) : ITenantEmployeeDirectoryRepository
    {
        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(
                eligible ? TenantEmployeeDirectoryEntry.Create(userId, TenantId, "TenantEmployee", true) : null
            );

        public Task UpsertAsync(
            Guid userId,
            Guid tenantId,
            string actorType,
            bool isActive,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task MarkActiveAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkInactiveAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkOffboardedAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeCustomers(params DomainCustomer[] seed) : ICustomerRepository
    {
        private readonly List<DomainCustomer> _all = [.. seed];

        public IReadOnlyList<DomainCustomer> All => _all;

        public Task<IReadOnlyList<DomainCustomer>> ListForAssignmentAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                _all.FindAll(c => c.TenantId == tenantId && ids.Contains(c.Id))
            );

        public Task<DomainCustomer?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) => throw new NotImplementedException();

        public Task<DomainCustomer?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(_all.FirstOrDefault(c => c.TenantId == tenantId && c.Id == customerId));

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

        public Task<OffboardingClientCounts> CountOffboardingImpactAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult(OffboardingClientCounts.None);

        public Task<Guid?> FindIdByOriginAsync(
            Guid tenantId,
            string sourceService,
            Guid sourceReferenceId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
