using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
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

    // El SQL ya excluye archivados: el doble devuelve el lote viejo para ejercitar el camino del dominio.
    [Fact]
    public async Task ArchivedCustomerInSweep_DoesNotThrow()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 1);
        var archived = customers.All[0];
        archived.Archive(Admin);
        customers.StalePrimaryBatch = [archived];
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        Assert.Null(archived.AssignedPreparerUserId);
        Assert.Empty(bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task Customer_already_reassigned_by_an_admin_keeps_their_preparer_and_announces_nothing()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Admin, count: 1);
        var reassigned = customers.All[0];
        customers.StalePrimaryBatch = [reassigned];
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        Assert.Equal(Admin, reassigned.AssignedPreparerUserId);
        Assert.DoesNotContain(reassigned.Assignments, a => a.UserId == Successor);
        Assert.Empty(bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
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

    /// <summary>
    /// Un cliente archivado no se trabaja, pero al reactivarlo no debe volver con un preparador retirado: el
    /// retiro lo suelta (sin asignar al sucesor: un archivado no admite preparador nuevo) y avisa a las
    /// proyecciones. La oficina elige preparador al reactivarlo.
    /// </summary>
    [Fact]
    public async Task Archived_clients_release_the_leaver_without_handing_over_to_the_successor()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        directory.Seed(Successor, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 1);
        var archived = customers.All[0];
        archived.Archive(Admin);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: Successor);

        Assert.Equal(CustomerStatus.Archived, archived.Status);
        Assert.Null(archived.AssignedPreparerUserId);
        Assert.Empty(archived.Assignments);
        Assert.Empty(bus.Published.OfType<CustomerPreparerAssignedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<CustomerPreparerUnassignedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Archived_clients_drop_the_leavers_extra_access_and_keep_the_primary()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        var shared = customers.SeedWithExtraAccess(TenantId, primaryUserId: Admin, extraUserId: Leaver);
        shared.Archive(Admin);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: null);

        Assert.DoesNotContain(shared.Assignments, a => a.UserId == Leaver);
        Assert.Equal(Admin, shared.AssignedPreparerUserId);
        Assert.Empty(bus.Published.OfType<CustomerPreparerUnassignedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Redelivery_after_the_archived_sweep_changes_nothing()
    {
        var directory = new FakeDirectory();
        directory.Seed(Leaver, TenantId, "TenantEmployee", isActive: true);
        var customers = new FakeCustomers();
        customers.SeedAssigned(TenantId, Leaver, count: 1);
        customers.All[0].Archive(Admin);
        await Consume(directory, customers, new FakeMessageBus(), successorUserId: null);
        var bus = new FakeMessageBus();

        await Consume(directory, customers, bus, successorUserId: null);

        Assert.Empty(bus.Published);
    }

    // Lo sembrado ya está guardado; lo emitido por el consumer sale por el bus como en Wolverine.
    private static async Task Consume(
        FakeDirectory directory,
        FakeCustomers customers,
        FakeMessageBus bus,
        Guid? successorUserId
    )
    {
        foreach (var customer in customers.All)
            customer.ClearDomainEvents();
        await CustomerUserOffboardedConsumer.Handle(
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
        await DomainEventReplay.DispatchIntoAsync(bus, customers, customers.All);
    }

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

        /// <summary>Lote que la consulta de primary devuelve una vez, tal como se leyó antes de un cambio.</summary>
        public List<DomainCustomer>? StalePrimaryBatch { get; set; }

        public void SeedAssigned(Guid tenantId, Guid preparerUserId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var name = PersonalName.Create("Client", $"Number{i}").Value;
                var email = EmailAddress.Create($"client-{Guid.NewGuid():N}@example.com").Value;
                var customer = DomainCustomer
                    .Register(tenantId, CustomerKind.Individual, name, null, email, null, Language.En, Guid.NewGuid())
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
                .Register(tenantId, CustomerKind.Individual, name, null, email, null, Language.En, Guid.NewGuid())
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
        )
        {
            if (StalePrimaryBatch is { } stale)
            {
                StalePrimaryBatch = null;
                return Task.FromResult<IReadOnlyList<DomainCustomer>>(stale);
            }

            return Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c =>
                        c.TenantId == tenantId
                        && c.Status != CustomerStatus.Archived
                        && c.AssignedPreparerUserId == preparerUserId
                        && c.Id > afterId
                    )
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToList()
            );
        }

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
                        && c.Status != CustomerStatus.Archived
                        && c.Id > afterId
                        && c.Assignments.Any(a => a.UserId == userId && !a.IsPrimary)
                    )
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToList()
            );

        public Task<IReadOnlyList<DomainCustomer>> ListArchivedAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c =>
                        c.TenantId == tenantId
                        && c.Status == CustomerStatus.Archived
                        && c.Id > afterId
                        && (c.AssignedPreparerUserId == userId || c.Assignments.Any(a => a.UserId == userId))
                    )
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToList()
            );

        public Task<DomainCustomer?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<DomainCustomer?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(All.FirstOrDefault(c => c.TenantId == tenantId && c.Id == customerId));

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

        public Task<int> CountActiveByAssignedPreparerAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct) =>
            Task.FromResult(0);

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

        public Task<IReadOnlyList<DomainCustomer>> ListForAssignmentAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);
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
