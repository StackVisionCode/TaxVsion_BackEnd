using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Consumers;
using TaxVision.Customer.Application.Customers.FromForms;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Domain.Permissions;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Publicador falso del comando de Tasks: cifra el payload con el codec real y entrega el comando al
/// consumer con dobles en memoria. No reemplaza las pruebas contra SQL Server y RabbitMQ reales.
/// </summary>
internal sealed class CustomerFormCreationHarness
{
    public static readonly Guid Tenant = Guid.Parse("8b7f0c1e-2a44-4f0d-9a51-3c0e6d2b7a10");
    public static readonly Guid Staff = Guid.Parse("5d2c9e47-0b1a-4c3e-8f7d-6a5b4c3d2e1f");

    public JsonProtectedPayloadCodec Codec { get; } =
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                        MasterKeyVersion = 1,
                    }
                )
            )
        );

    public List<string> Steps { get; } = [];
    public InMemoryCustomers Repository { get; }
    public Detector Duplicates { get; }
    public EmployeeDirectory Employees { get; } = new();
    public PermissionProjections Grants { get; } = new();
    public CreationLock Lock { get; }
    public CountingUnitOfWork UnitOfWork { get; } = new();
    public FakeMessageBus Bus { get; } = new();
    public RecordingLogger Logger { get; } = new();

    public CustomerFormCreationHarness()
    {
        Repository = new InMemoryCustomers(Steps);
        Duplicates = new Detector(Steps);
        Lock = new CreationLock(Steps);
        Employees.Add(Staff, Tenant, "TenantEmployee");
        Grants.Grant(Tenant, Staff, "customers.manage");
    }

    public CustomerCreationFromFormRequestedIntegrationEvent Command(
        Guid? formRequestId = null,
        IReadOnlyList<ClientFormTargetValue>? values = null,
        Guid? requestedBy = null
    )
    {
        var sourceReferenceId = formRequestId ?? Guid.NewGuid();
        var idempotencyKey = $"{sourceReferenceId:N}:r1:create_client";
        var participant = new ClientFormParticipant { Kind = "External", ParticipantId = Guid.NewGuid() };
        var binding = new ProtectedPayloadBinding(
            Tenant,
            sourceReferenceId,
            participant,
            IdempotencyKey: idempotencyKey
        );
        return new CustomerCreationFromFormRequestedIntegrationEvent
        {
            TenantId = Tenant,
            CorrelationId = "corr-test",
            RequestId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
            SourceReferenceId = sourceReferenceId,
            RevisionNumber = 1,
            RequestedByUserId = requestedBy ?? Staff,
            TargetCatalogVersion = 1,
            Participant = participant,
            Payload = Codec.Protect(
                ClientFormProtectedPayloadSchemas.CustomerCreation,
                binding,
                new CustomerCreationFromFormPayload(values ?? IndividualValues())
            ),
        };
    }

    public Task SendAsync(CustomerCreationFromFormRequestedIntegrationEvent command) =>
        CustomerCreationFromFormRequestedConsumer.Handle(
            command,
            Codec,
            Repository,
            Duplicates,
            Employees,
            Grants,
            Lock,
            UnitOfWork,
            Bus,
            new TestCorrelation(),
            Logger,
            CancellationToken.None
        );

    public static List<ClientFormTargetValue> IndividualValues(string email = "ada@example.com") =>
        [
            new("customer.kind", 1, "Individual"),
            new("customer.person.first_name", 1, "Ada"),
            new("customer.person.last_name", 1, "Lovelace"),
            new("customer.primary_email", 1, email),
            new("customer.primary_phone", 1, "+18095551234"),
            new("customer.date_of_birth", 1, "1985-12-10"),
            new("customer.language", 1, "En"),
            new("customer.contact.sms_enabled", 1, "false"),
        ];

    internal sealed class InMemoryCustomers(List<string> steps) : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [];

        public Task AddAsync(DomainCustomer customer, CancellationToken ct)
        {
            steps.Add("create");
            All.Add(customer);
            return Task.CompletedTask;
        }

        public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.Id == id));

        public Task<Guid?> FindIdByOriginAsync(
            Guid tenantId,
            string sourceService,
            Guid sourceReferenceId,
            CancellationToken ct
        ) =>
            Task.FromResult(
                All.Find(c =>
                    c.TenantId == tenantId
                    && c.Origin?.SourceService == sourceService
                    && c.Origin.SourceReferenceId == sourceReferenceId
                )?.Id
            );

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

    /// <summary>Devuelve el match configurado; el detector real no cambia y no se prueba aquí.</summary>
    internal sealed class Detector(List<string> steps) : ICustomerDuplicateDetector
    {
        public DuplicateMatch? Match { get; set; }
        public int Calls { get; private set; }

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
        )
        {
            Calls++;
            steps.Add("detect");
            return Task.FromResult(Match);
        }
    }

    internal sealed class EmployeeDirectory : ITenantEmployeeDirectoryRepository
    {
        private readonly Dictionary<Guid, TenantEmployeeDirectoryEntry> _entries = [];
        public int Reads { get; private set; }

        public TenantEmployeeDirectoryEntry Add(Guid userId, Guid tenantId, string actorType, bool isActive = true)
        {
            var entry = TenantEmployeeDirectoryEntry.Create(userId, tenantId, actorType, isActive);
            _entries[userId] = entry;
            return entry;
        }

        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(_entries.GetValueOrDefault(userId));
        }

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

    internal sealed class PermissionProjections : IUserPermissionsProjectionRepository
    {
        private readonly Dictionary<(Guid, Guid), UserPermissionsProjection> _projections = [];

        public void Grant(Guid tenantId, Guid userId, params string[] codes) =>
            _projections[(tenantId, userId)] = UserPermissionsProjection.Create(tenantId, userId, 1, codes, []);

        public void Revoke(Guid tenantId, Guid userId) => _projections.Remove((tenantId, userId));

        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_projections.GetValueOrDefault((tenantId, userId)));

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserPermissionsProjection>>([]);
    }

    internal sealed class CreationLock(List<string> steps) : ICustomerCreationLock
    {
        public List<string> Resources { get; } = [];

        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
        {
            steps.Add("lock");
            Resources.Add(CustomerCreationLockResource.For(tenantId, normalizedEmail));
            return Task.CompletedTask;
        }
    }

    internal sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    internal sealed class RecordingLogger : ILogger<CustomerCreationFromFormRequestedIntegrationEvent>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TestCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
