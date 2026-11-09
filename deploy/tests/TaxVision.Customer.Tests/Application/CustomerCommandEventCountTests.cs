using System.Reflection;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Avatars;
using TaxVision.Customer.Application.Customers.Commands.Activate;
using TaxVision.Customer.Application.Customers.Commands.Archive;
using TaxVision.Customer.Application.Customers.Commands.AssignPreparer;
using TaxVision.Customer.Application.Customers.Commands.BulkAssign;
using TaxVision.Customer.Application.Customers.Commands.BulkChangeStatus;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Customers.Commands.Deactivate;
using TaxVision.Customer.Application.Customers.Commands.GrantAccess;
using TaxVision.Customer.Application.Customers.Commands.Reactivate;
using TaxVision.Customer.Application.Customers.Commands.RevokeAccess;
using TaxVision.Customer.Application.Customers.Commands.UnassignPreparer;
using TaxVision.Customer.Application.Customers.Commands.Update;
using TaxVision.Customer.Application.Customers.Portal;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Employees;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Un aviso por acción: cuenta lo emitido por cada comando, sumando lo publicado a mano y lo devuelto por los
/// handlers de eventos de dominio. Si quedan los dos caminos, el aviso sale dos veces y esto falla.
/// </summary>
public sealed class CustomerCommandEventCountTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();

    [Fact]
    public async Task Create_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(typeof(CreateCustomerHandler), CreateCommand("maria@example.com"));

        var emitted = await fixture.EmittedAsync();
        var created = Assert.Single(emitted.OfType<CustomerCreatedIntegrationEvent>());
        Assert.Equal(Staff, created.CreatedByUserId);
        Assert.Single(emitted.OfType<CustomerPreparerAssignedIntegrationEvent>());
        Assert.Equal(
            [Staff],
            Assert.Single(emitted.OfType<CustomerAssignmentsChangedIntegrationEvent>()).AssigneeUserIds
        );
        Assert.Empty(emitted.OfType<CustomerUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task CreateOverwrite_EmitsOneUpdatedAndNoCreated()
    {
        var existing = NewCustomer();
        var fixture = new Fixture(existing) { Duplicate = existing };

        await fixture.RunAsync(
            typeof(CreateCustomerHandler),
            CreateCommand("other@example.com") with
            {
                Overwrite = true,
            }
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Equal(
            "other@example.com",
            Assert.Single(emitted.OfType<CustomerUpdatedIntegrationEvent>()).PrimaryEmail
        );
        Assert.Empty(emitted.OfType<CustomerCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task Update_EmitsExactlyOneUpdated()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(UpdateCustomerHandler),
            new UpdateCustomerCommand(
                TenantId,
                customer.Id,
                Staff,
                Language.Es,
                null,
                "maria.new@example.com",
                "+13055550100",
                "Maria",
                null,
                "Perez",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                CallerCanViewAllCustomers: true,
                EmailEnabled: true,
                SmsEnabled: false
            )
        );

        var updated = Assert.Single((await fixture.EmittedAsync()).OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Equal("maria.new@example.com", updated.PrimaryEmail);
        Assert.False(updated.SmsEnabled);
    }

    [Fact]
    public async Task Archive_EmitsExactlyOneArchived()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(ArchiveCustomerHandler),
            new ArchiveCustomerCommand(TenantId, customer.Id, Staff)
        );

        Assert.Equal(
            Staff,
            Assert.Single((await fixture.EmittedAsync()).OfType<CustomerArchivedIntegrationEvent>()).ArchivedByUserId
        );
    }

    [Fact]
    public async Task Reactivate_EmitsExactlyOneReactivated()
    {
        var customer = NewCustomer();
        customer.Archive(Staff);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(ReactivateCustomerHandler),
            new ReactivateCustomerCommand(TenantId, customer.Id, Staff)
        );

        Assert.Single((await fixture.EmittedAsync()).OfType<CustomerReactivatedIntegrationEvent>());
    }

    [Fact]
    public async Task Deactivate_EmitsExactlyOneDeactivated()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(DeactivateCustomerHandler),
            new DeactivateCustomerCommand(TenantId, customer.Id, Staff)
        );

        Assert.Single((await fixture.EmittedAsync()).OfType<CustomerDeactivatedIntegrationEvent>());
    }

    [Fact]
    public async Task Activate_EmitsExactlyOneActivated()
    {
        var customer = NewCustomer();
        customer.Deactivate(Staff);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(ActivateCustomerHandler),
            new ActivateCustomerCommand(TenantId, customer.Id, Staff)
        );

        Assert.Single((await fixture.EmittedAsync()).OfType<CustomerActivatedIntegrationEvent>());
    }

    [Fact]
    public async Task AssignPreparer_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(AssignPreparerHandler),
            new AssignPreparerCommand(TenantId, customer.Id, Preparer, Staff)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<CustomerPreparerAssignedIntegrationEvent>());
        var snapshot = Assert.Single(emitted.OfType<CustomerAssignmentsChangedIntegrationEvent>());
        Assert.Equal(Preparer, snapshot.PrimaryUserId);
        Assert.Equal(customer.UpdatedAtUtc, snapshot.Version);
    }

    [Fact]
    public async Task UnassignPreparer_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Preparer, Staff);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(UnassignPreparerHandler),
            new UnassignPreparerCommand(TenantId, customer.Id, Staff)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<CustomerPreparerUnassignedIntegrationEvent>());
        Assert.Empty(Assert.Single(emitted.OfType<CustomerAssignmentsChangedIntegrationEvent>()).AssigneeUserIds);
    }

    [Fact]
    public async Task GrantAccess_EmitsExactlyOneSnapshot()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(GrantCustomerAccessHandler),
            new GrantCustomerAccessCommand(TenantId, customer.Id, Preparer, Staff)
        );

        Assert.Single((await fixture.EmittedAsync()).OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task RevokeAccess_EmitsExactlyOneSnapshot()
    {
        var customer = NewCustomer();
        customer.GrantAccess(Preparer, Staff);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(RevokeCustomerAccessHandler),
            new RevokeCustomerAccessCommand(TenantId, customer.Id, Preparer, Staff)
        );

        Assert.Single((await fixture.EmittedAsync()).OfType<CustomerAssignmentsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task BulkAssign_EmitsOneSnapshotPerCustomer()
    {
        var first = NewCustomer();
        var second = NewCustomer();
        second.AssignPreparer(Staff, Staff);
        var fixture = new Fixture(first, second);

        await fixture.RunAsync(
            typeof(BulkAssignCustomersHandler),
            new BulkAssignCustomersCommand(TenantId, Preparer, [first.Id, second.Id], Staff)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<CustomerAssignmentsChangedIntegrationEvent>(), e => e.CustomerId == first.Id);
        Assert.Single(emitted.OfType<CustomerAssignmentsChangedIntegrationEvent>(), e => e.CustomerId == second.Id);
        Assert.Single(emitted.OfType<CustomerPreparerAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task BulkArchive_EmitsOneArchivedPerCustomer()
    {
        var first = NewCustomer();
        var second = NewCustomer();
        var fixture = new Fixture(first, second);

        await fixture.RunAsync(
            typeof(BulkChangeStatusHandler),
            new BulkChangeStatusCommand(TenantId, Staff, BulkStatusAction.Archive, [first.Id, second.Id], null)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<CustomerArchivedIntegrationEvent>(), e => e.CustomerId == first.Id);
        Assert.Single(emitted.OfType<CustomerArchivedIntegrationEvent>(), e => e.CustomerId == second.Id);
    }

    [Fact]
    public async Task PortalPhone_EmitsExactlyOneUpdated()
    {
        var customer = NewCustomer();
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(UpdateMyCustomerPhoneHandler),
            new UpdateMyCustomerPhoneCommand(TenantId, customer.Id, Staff, "+13055550111")
        );

        Assert.Equal(
            "+13055550111",
            Assert.Single((await fixture.EmittedAsync()).OfType<CustomerUpdatedIntegrationEvent>()).PrimaryPhone
        );
    }

    [Fact]
    public async Task RemoveAvatar_EmitsExactlyOneUpdated()
    {
        var customer = WithPicture(out _);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(RemoveCustomerAvatarHandler),
            new RemoveCustomerAvatarCommand(
                TenantId,
                customer.Id,
                Staff,
                CustomerAvatarCaller.Staff,
                CallerCanViewAllCustomers: true
            )
        );

        Assert.Null(
            Assert.Single((await fixture.EmittedAsync()).OfType<CustomerUpdatedIntegrationEvent>()).AvatarFileId
        );
    }

    [Fact]
    public async Task AvatarConfirmed_EmitsExactlyOneUpdated()
    {
        var customer = NewCustomer();
        var fileId = Guid.NewGuid();
        customer.BeginProfilePictureChange(fileId, Staff, Now);
        var fixture = new Fixture(customer);

        await fixture.RunAsync(
            typeof(CustomerAvatarScanResultConsumer),
            new FileAvailableIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = $"tenants/{TenantId}/customer/{customer.Id}/avatars/{fileId}.webp",
                ContentType = "image/webp",
                SizeBytes = 42,
                ChecksumSha256 = "abc",
                CreatedBy = Staff,
                OwnerType = "Customer",
                OwnerId = customer.Id,
                FolderType = "Avatars",
            }
        );

        var updated = Assert.Single((await fixture.EmittedAsync()).OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Equal(fileId, updated.AvatarFileId);
        Assert.Equal(Staff, updated.ModifiedByUserId);
    }

    private static CreateCustomerCommand CreateCommand(string email) =>
        new(
            TenantId,
            Staff,
            CustomerKind.Individual,
            "Maria",
            null,
            "Perez",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            email,
            null,
            Language.En
        );

    /// <summary>Corre el handler resolviendo sus parámetros por tipo, sin atarse a su firma.</summary>
    private sealed class Fixture
    {
        public Fixture(params DomainCustomer[] seed)
        {
            // Lo sembrado ya está guardado: sus eventos ya salieron.
            foreach (var customer in seed)
                customer.ClearDomainEvents();
            Customers = new AllCustomers(seed);
        }

        public AllCustomers Customers { get; }
        public FakeMessageBus Bus { get; } = new();
        public DomainCustomer? Duplicate { get; init; }

        public async Task<IReadOnlyList<object>> EmittedAsync() =>
            [.. Bus.Published, .. await DomainEventReplay.DispatchAsync(Customers.All, Customers)];

        public async Task RunAsync(Type handler, object command)
        {
            var services = new List<object>
            {
                Customers,
                Bus,
                new RecordingUnitOfWork(Bus),
                new FixedCorrelationContext(),
                new Duplicates(Duplicate),
                new EligibleStaff(),
                new CustomerFormCreationHarness.CreationLock([]),
            };
            var method = handler
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == command.GetType());
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : IsLogger(p.ParameterType)
                        ? Activator.CreateInstance(
                            typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments())
                        )
                    : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();
            var task = (Task)method.Invoke(null, args)!;
            await task;
            if (task.GetType().GetProperty("Result")?.GetValue(task) is BuildingBlocks.Results.Result result)
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        private static bool IsLogger(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
    }

    private sealed class Duplicates(DomainCustomer? existing) : ICustomerDuplicateDetector
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
        ) =>
            Task.FromResult(
                existing is null || existing.Id == excludeCustomerId
                    ? null
                    : new DuplicateMatch(0, existing.Id, existing.DisplayName, "email")
            );
    }

    private sealed class EligibleStaff : ITenantEmployeeDirectoryRepository
    {
        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<TenantEmployeeDirectoryEntry?>(
                TenantEmployeeDirectoryEntry.Create(userId, TenantId, "TenantEmployee", true)
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

    /// <summary>Repositorio en memoria con las consultas que usan estos comandos.</summary>
    internal sealed class AllCustomers(IEnumerable<DomainCustomer> seed) : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [.. seed];

        public Task<DomainCustomer?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.TenantId == tenantId && c.Id == id));

        public Task<DomainCustomer?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.TenantId == tenantId && c.Id == customerId));

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c => c.TenantId == tenantId && ids.Contains(c.Id))
            );

        public Task<IReadOnlyList<DomainCustomer>> ListForAssignmentAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                All.FindAll(c => c.TenantId == tenantId && ids.Contains(c.Id) && c.Status != CustomerStatus.Archived)
            );

        public Task AddAsync(DomainCustomer customer, CancellationToken ct)
        {
            All.Add(customer);
            return Task.CompletedTask;
        }

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(Guid t, string b, Guid? e, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(Guid t, string b, Guid? e, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid t,
            IReadOnlyCollection<string> b,
            Guid? e,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid t,
            IReadOnlyCollection<string> b,
            Guid? e,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid t,
            Guid p,
            int b,
            Guid a,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<IReadOnlyList<DomainCustomer>> ListNonPrimaryAssignedAsync(
            Guid t,
            Guid u,
            int b,
            Guid a,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<int> CountActiveByAssignedPreparerAsync(Guid t, Guid p, CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<DomainCustomer>> ListArchivedAssignedAsync(
            Guid t,
            Guid u,
            int b,
            Guid a,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<OffboardingClientCounts> CountOffboardingImpactAsync(Guid t, Guid u, CancellationToken ct) =>
            Task.FromResult(OffboardingClientCounts.None);

        public Task<Guid?> FindIdByOriginAsync(Guid t, string s, Guid r, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);
    }
}
