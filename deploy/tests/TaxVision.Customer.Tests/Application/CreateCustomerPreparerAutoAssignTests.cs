using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Employees;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// B-13: el alta manual auto-asigna al creador como preparador primary solo si puede serlo. Los caminos
/// elegible, admin y "aún no proyectado" se caracterizan tal cual estaban; el no elegible se crea sin asignar.
/// </summary>
public sealed class CreateCustomerPreparerAutoAssignTests
{
    private static readonly Guid Tenant = Guid.Parse("8a2f6c1e-3b4d-4e5f-9a0b-1c2d3e4f5a6b");
    private static readonly Guid OtherTenant = Guid.Parse("0f1e2d3c-4b5a-4968-8776-655443322110");
    private static readonly Guid Creator = Guid.Parse("5c6d7e8f-9a0b-4c1d-8e2f-3a4b5c6d7e8f");

    // ---- Caracterización: comportamiento previo que no cambia ----

    [Fact]
    public async Task An_eligible_employee_creator_becomes_the_primary_preparer()
    {
        var run = await CreateAsync(Entry("TenantEmployee"));

        Assert.True(run.Result.IsSuccess);
        Assert.Equal(Creator, run.Customer.AssignedPreparerUserId);
        var assigned = Assert.Single(run.Bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
        Assert.Equal(Creator, assigned.PreparerUserId);
        var snapshot = Assert.Single(run.Bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
        Assert.Equal(Creator, snapshot.PrimaryUserId);
    }

    [Fact]
    public async Task A_creator_not_yet_in_the_directory_is_still_assigned()
    {
        var run = await CreateAsync(entry: null);

        Assert.True(run.Result.IsSuccess);
        Assert.Equal(Creator, run.Customer.AssignedPreparerUserId);
        Assert.Single(run.Bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
    }

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("PlatformAdmin")]
    public async Task An_admin_creator_is_not_assigned(string actorType)
    {
        var run = await CreateAsync(Entry(actorType));

        Assert.True(run.Result.IsSuccess);
        Assert.Null(run.Customer.AssignedPreparerUserId);
        Assert.Empty(run.Bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
    }

    // ---- B-13: creador conocido y no elegible → se crea, sin asignar ----

    public static TheoryData<string> NotEligibleCreators => ["offboarded", "inactive", "portal", "other-tenant"];

    [Theory]
    [MemberData(nameof(NotEligibleCreators))]
    public async Task A_non_eligible_creator_still_creates_the_customer_but_is_not_assigned(string kind)
    {
        var entry = kind switch
        {
            "offboarded" => Offboarded(),
            "inactive" => Entry("TenantEmployee", isActive: false),
            "portal" => Entry("CustomerPortal"),
            "other-tenant" => Entry("TenantEmployee", tenantId: OtherTenant),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var run = await CreateAsync(entry);

        Assert.True(run.Result.IsSuccess);
        Assert.Null(run.Customer.AssignedPreparerUserId);
        Assert.Empty(run.Customer.Assignments);
        Assert.Single(run.Bus.Published.OfType<CustomerCreatedIntegrationEvent>());
        Assert.Empty(run.Bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
        Assert.Empty(run.Bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    private static TenantEmployeeDirectoryEntry Entry(string actorType, bool isActive = true, Guid? tenantId = null) =>
        TenantEmployeeDirectoryEntry.Create(Creator, tenantId ?? Tenant, actorType, isActive);

    private static TenantEmployeeDirectoryEntry Offboarded()
    {
        var entry = Entry("TenantEmployee");
        entry.MarkOffboarded();
        return entry;
    }

    private static async Task<Run> CreateAsync(TenantEmployeeDirectoryEntry? entry)
    {
        var repo = new CapturingCustomers();
        var bus = new FakeMessageBus();
        var result = await CreateCustomerHandler.Handle(
            new CreateCustomerCommand(
                Tenant,
                Creator,
                CustomerKind.Individual,
                "Ada",
                null,
                "Lovelace",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "ada@example.com",
                null,
                Language.En,
                false
            ),
            repo,
            new NoDuplicates(),
            new SingleEntryDirectory(entry),
            new NoOpUnitOfWork(),
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );
        return new Run(result, repo.All.Single(), bus);
    }

    private sealed record Run(Result<CustomerResponse> Result, DomainCustomer Customer, FakeMessageBus Bus);

    private sealed class SingleEntryDirectory(TenantEmployeeDirectoryEntry? entry) : ITenantEmployeeDirectoryRepository
    {
        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(entry?.UserId == userId ? entry : null);

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

    private sealed class NoDuplicates : ICustomerDuplicateDetector
    {
        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DuplicateMatch>>([]);

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => Task.FromResult<DuplicateMatch?>(null);
    }

    private sealed class CapturingCustomers : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [];

        public Task AddAsync(DomainCustomer customer, CancellationToken ct)
        {
            All.Add(customer);
            return Task.CompletedTask;
        }

        public Task<DomainCustomer?> GetByIdAsync(Guid customerId, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.Id == customerId));

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>(All.FindAll(c => ids.Contains(c.Id)));

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeRelationId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid tenantId,
            Guid preparerUserId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
