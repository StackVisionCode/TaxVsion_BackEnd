using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Employees.Consumers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Employees;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Al retirar (offboard) a un preparador: el directorio queda retirado (terminal) y sus clientes
/// se reasignan al sucesor elegible, o se desasignan si no hay sucesor elegible.
/// </summary>
public sealed class CustomerUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Eligible_successor_reassigns_all_the_leavers_customers()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 3);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        Assert.True(directory.Get(Leaver)!.IsOffboarded);
        Assert.All(customers.All, c => Assert.Equal(Successor, c.AssignedPreparerUserId));
        Assert.Equal(3, bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task No_successor_unassigns_all_the_leavers_customers()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 2);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: null);

        Assert.All(customers.All, c => Assert.Null(c.AssignedPreparerUserId));
        Assert.Equal(2, bus.Published.OfType<CustomerPreparerUnassignedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Ineligible_successor_falls_back_to_unassign()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        // Sucesor desactivado → no elegible → se desasigna en vez de asignar a un preparador invalido.
        var successor = directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        successor.MarkInactive();
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 1);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        Assert.Null(customers.All[0].AssignedPreparerUserId);
        Assert.Single(bus.Published.OfType<CustomerPreparerUnassignedIntegrationEvent>());
    }

    [Fact]
    public async Task Offboard_marks_the_directory_entry_terminal()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: null);

        var entry = directory.Get(Leaver)!;
        Assert.True(entry.IsOffboarded);
        Assert.False(entry.IsEligiblePreparer);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handover_leaves_the_successor_primary_and_strips_the_leaver()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 1);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        var assignments = customers.All[0].Assignments;
        Assert.DoesNotContain(assignments, a => a.UserId == Leaver); // el saliente no conserva acceso
        Assert.Contains(assignments, a => a.UserId == Successor && a.IsPrimary);
    }

    [Fact]
    public async Task Offboard_revokes_the_leavers_extra_access()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        var shared = customers.SeedWithExtraAccess(TenantId, primaryUserId: Admin, extraUserId: Leaver);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: null);

        Assert.DoesNotContain(shared.Assignments, a => a.UserId == Leaver); // acceso adicional revocado
        Assert.Contains(shared.Assignments, a => a.UserId == Admin && a.IsPrimary); // el primary intacto
        Assert.Single(bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    private static Task Consume(
        FakeDirectory directory,
        FakeCustomers customers,
        FakeMessageBus bus,
        Guid? successorUserId
    ) =>
        CustomerUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = Leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                OffboardedByUserId = Admin,
                SuccessorUserId = successorUserId,
                RemovedAtUtc = DateTime.UtcNow,
            },
            directory,
            customers,
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            NullLogger<TenantEmployeeDirectoryEntry>.Instance,
            CancellationToken.None
        );

    private sealed class FakeDirectory : ITenantEmployeeDirectoryRepository
    {
        private readonly Dictionary<Guid, TenantEmployeeDirectoryEntry> _entries = [];

        public TenantEmployeeDirectoryEntry Seed(Guid userId, Guid tenantId, string actorType, bool isActive)
        {
            var entry = TenantEmployeeDirectoryEntry.Create(userId, tenantId, actorType, isActive);
            _entries[userId] = entry;
            return entry;
        }

        public TenantEmployeeDirectoryEntry? Get(Guid userId) => _entries.GetValueOrDefault(userId);

        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_entries.GetValueOrDefault(userId));

        public Task UpsertAsync(
            Guid userId,
            Guid tenantId,
            string actorType,
            bool isActive,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task MarkActiveAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task MarkInactiveAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkOffboardedAsync(Guid userId, CancellationToken ct = default)
        {
            _entries.GetValueOrDefault(userId)?.MarkOffboarded();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCustomers : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [];

        public void SeedAssigned(Guid tenantId, Guid preparerUserId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var name = PersonalName.Create("Client", $"Number{i}").Value;
                var email = EmailAddress.Create($"client-{Guid.NewGuid():N}@example.com").Value;
                var customer = DomainCustomer
                    .Register(
                        tenantId,
                        CustomerKind.Individual,
                        name,
                        null,
                        email,
                        null,
                        Language.En,
                        PreferredChannel.Email,
                        Guid.NewGuid()
                    )
                    .Value;
                customer.AssignPreparer(preparerUserId, Guid.NewGuid());
                All.Add(customer);
            }
        }

        // Un cliente con primary = otro y acceso ADICIONAL (no-primary) para el saliente.
        public DomainCustomer SeedWithExtraAccess(Guid tenantId, Guid primaryUserId, Guid extraUserId)
        {
            var name = PersonalName.Create("Shared", "Client").Value;
            var email = EmailAddress.Create($"shared-{Guid.NewGuid():N}@example.com").Value;
            var customer = DomainCustomer
                .Register(
                    tenantId,
                    CustomerKind.Individual,
                    name,
                    null,
                    email,
                    null,
                    Language.En,
                    PreferredChannel.Email,
                    Guid.NewGuid()
                )
                .Value;
            customer.AssignPreparer(primaryUserId, Guid.NewGuid());
            customer.GrantAccess(extraUserId, Guid.NewGuid());
            All.Add(customer);
            return customer;
        }

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid tenantId,
            Guid preparerUserId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c => c.TenantId == tenantId && c.AssignedPreparerUserId == preparerUserId && c.Id > afterId)
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToList()
            );

        public Task<IReadOnlyList<DomainCustomer>> ListNonPrimaryAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c =>
                        c.TenantId == tenantId
                        && c.Id > afterId
                        && c.Assignments.Any(a => a.UserId == userId && !a.IsPrimary)
                    )
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToList()
            );

        public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();

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

        public Task AddAsync(DomainCustomer customer, CancellationToken ct) => throw new NotImplementedException();
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
