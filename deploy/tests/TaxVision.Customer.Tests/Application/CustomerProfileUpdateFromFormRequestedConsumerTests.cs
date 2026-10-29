using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Consumers;
using TaxVision.Customer.Domain.Audit;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class CustomerProfileUpdateFromFormRequestedConsumerTests
{
    private static readonly Guid Tenant = Guid.Parse("8b7f0c1e-2a44-4f0d-9a51-3c0e6d2b7a10");
    private static readonly Guid Staff = Guid.Parse("5d2c9e47-0b1a-4c3e-8f7d-6a5b4c3d2e1f");

    [Fact]
    public async Task Applies_phone_and_address_and_publishes_applied()
    {
        var harness = new Harness();
        var customer = harness.SeedCustomer();

        await harness.SendAsync(customer.Id, Values(Phone("+18095559999"), HomeAddress()));

        Assert.Equal("+18095559999", customer.PrimaryPhone!.E164Value);
        Assert.Single(customer.Addresses);
        var applied = Assert.IsType<CustomerProfileUpdateFromFormAppliedIntegrationEvent>(
            Assert.Single(harness.Bus.Published)
        );
        Assert.Empty(applied.Failures);
        Assert.Contains("customer.primary_phone", applied.AppliedTargets);
        Assert.Contains("customer.address.home", applied.AppliedTargets);
    }

    [Fact]
    public async Task Rejects_when_requester_lacks_customers_manage()
    {
        var harness = new Harness();
        var customer = harness.SeedCustomer();
        harness.Permissions.Revoke(Tenant, Staff); // ya no tiene customers.manage

        await harness.SendAsync(customer.Id, Values(Phone("+18095559999")));

        Assert.IsType<CustomerProfileUpdateFromFormRejectedIntegrationEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal("+18095551234", customer.PrimaryPhone!.E164Value); // sin cambios
    }

    [Fact]
    public async Task Filing_status_from_non_admin_fails_the_fiscal_target()
    {
        var harness = new Harness();
        var customer = harness.SeedCustomer(withFiscalProfile: true);

        // Staff tiene manage pero no es admin: el destino fiscal no se aplica.
        await harness.SendAsync(customer.Id, Values(Filing("MarriedJoint")));

        var rejected = Assert.IsType<CustomerProfileUpdateFromFormRejectedIntegrationEvent>(
            Assert.Single(harness.Bus.Published)
        );
        Assert.Contains(
            rejected.Failures,
            failure =>
                failure.Target == "customer.fiscal.filing_status"
                && failure.Code == CustomerProfileUpdateFromFormOutcomes.NotAuthorized
        );
        Assert.Equal(FilingStatus.Single, customer.FiscalProfile!.FilingStatus);
    }

    [Fact]
    public async Task Admin_applies_tax_identifier_end_to_end_and_preserves_unmapped_fields()
    {
        var harness = new Harness();
        harness.Employees.Add(Staff, Tenant, "TenantAdmin"); // admin
        var customer = harness.SeedCustomer(withFiscalProfile: true);

        await harness.SendAsync(customer.Id, Values(TaxId("123-45-6789", "Ssn")));

        // SSN cifrado de punta a punta: solo el ciphertext y el last4 quedan en el perfil.
        Assert.Equal("6789", customer.FiscalProfile!.TaxIdentifierLast4);
        Assert.NotNull(customer.FiscalProfile.TaxIdentifierCipher);
        // Rev. 6.1: filing, AGI y returning actuales se conservan al reemplazar el perfil.
        Assert.Equal(FilingStatus.Single, customer.FiscalProfile.FilingStatus);
        Assert.Equal(50000m, customer.FiscalProfile.PriorYearAgi);
        Assert.True(customer.FiscalProfile.IsReturningCustomer);
        var applied = Assert.IsType<CustomerProfileUpdateFromFormAppliedIntegrationEvent>(
            Assert.Single(harness.Bus.Published)
        );
        Assert.Contains("customer.fiscal.tax_identifier", applied.AppliedTargets);
    }

    [Fact]
    public async Task Dependents_are_not_duplicated_on_replay()
    {
        var harness = new Harness();
        var customer = harness.SeedCustomer();
        var values = Values(Dependents());

        await harness.SendAsync(customer.Id, values);
        await harness.SendAsync(customer.Id, values); // idempotente

        Assert.Single(customer.Relations);
    }

    private static IReadOnlyList<ClientFormTargetValue> Values(params ClientFormTargetValue[] values) => values;

    private static ClientFormTargetValue Phone(string e164) => new("customer.primary_phone", 1, e164);

    private static ClientFormTargetValue HomeAddress() =>
        new(
            "customer.address.home",
            1,
            Address: new ClientFormAddressValue("1 Main St", null, "City", "CA", "90001", "US")
        );

    private static ClientFormTargetValue Filing(string status) => new("customer.fiscal.filing_status", 1, status);

    private static ClientFormTargetValue TaxId(string value, string kind) =>
        new("customer.fiscal.tax_identifier", 1, value, Kind: kind);

    private static ClientFormTargetValue Dependents() =>
        new(
            "customer.relation.dependents",
            1,
            Rows:
            [
                new ClientFormTargetRow(
                    new Dictionary<string, string>
                    {
                        ["first_name"] = "Kid",
                        ["last_name"] = "Lovelace",
                        ["date_of_birth"] = "2015-01-01",
                    }
                ),
            ]
        );

    private sealed class Harness
    {
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

        public InMemoryCustomers Repository { get; } = new();
        public CustomerFormCreationHarness.EmployeeDirectory Employees { get; } = new();
        public CustomerFormCreationHarness.PermissionProjections Permissions { get; } = new();
        public FakeProtector Protector { get; } = new();
        public AuditWriter Audit { get; } = new();
        public CustomerFormCreationHarness.CountingUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Harness()
        {
            Employees.Add(Staff, Tenant, "TenantEmployee");
            Permissions.Grant(Tenant, Staff, "customers.manage");
        }

        public DomainCustomer SeedCustomer(bool withFiscalProfile = false)
        {
            var customer = DomainCustomer
                .Register(
                    Tenant,
                    CustomerKind.Individual,
                    PersonalName.Create("Ada", "Lovelace").Value,
                    null,
                    EmailAddress.Create("ada@example.com").Value,
                    PhoneNumber.Create("+18095551234").Value,
                    Language.En,
                    Staff
                )
                .Value;
            if (withFiscalProfile)
                customer.SetFiscalProfile(
                    FiscalSubjectKind.Individual,
                    Encoding.UTF8.GetBytes("111111111"),
                    "bi:111111111",
                    "1111",
                    FilingStatus.Single,
                    50000m,
                    isReturningCustomer: true,
                    refundBankAccountCipher: null,
                    refundBankRoutingCipher: null,
                    byUserId: Staff
                );
            Repository.All.Add(customer);
            return customer;
        }

        public Task SendAsync(Guid customerId, IReadOnlyList<ClientFormTargetValue> values)
        {
            var sourceReferenceId = Guid.NewGuid();
            var idempotencyKey = sourceReferenceId.ToString("N");
            var participant = new ClientFormParticipant { Kind = "Customer", CustomerId = customerId };
            var binding = new ProtectedPayloadBinding(
                Tenant,
                sourceReferenceId,
                participant,
                IdempotencyKey: idempotencyKey
            );
            var actorType = Employees.GetByUserIdAsync(Staff).Result?.ActorType ?? "TenantEmployee";
            var command = new CustomerProfileUpdateFromFormRequestedIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "corr-test",
                CommandId = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
                SourceReferenceId = sourceReferenceId,
                RevisionNumber = 1,
                TargetCatalogVersion = 1,
                Participant = participant,
                Payload = Codec.Protect(
                    ClientFormProtectedPayloadSchemas.CustomerProfileUpdate,
                    binding,
                    new CustomerProfileUpdateFromFormPayload(Staff, actorType, values)
                ),
            };
            return CustomerProfileUpdateFromFormRequestedConsumer.Handle(
                command,
                Codec,
                Repository,
                Employees,
                Permissions,
                Protector,
                Audit,
                UnitOfWork,
                Bus,
                new TestCorrelation(),
                NullLogger<CustomerProfileUpdateFromFormRequestedIntegrationEvent>.Instance,
                CancellationToken.None
            );
        }
    }

    // Protector determinista: cifra como UTF8 y el blind index como "bi:"+valor. No es criptografía real.
    internal sealed class FakeProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"bi:{plainText}";
    }

    internal sealed class AuditWriter : ICustomerAuditWriter
    {
        public List<CustomerAuditLog> Logs { get; } = [];

        public Task AddAsync(CustomerAuditLog log, CancellationToken ct = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemoryCustomers : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [];
        public string? ConflictBlindIndex { get; set; }

        public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.Id == id));

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) =>
            Task.FromResult<Guid?>(
                ConflictBlindIndex is not null && ConflictBlindIndex == blindIndex ? Guid.NewGuid() : null
            );

        public Task AddAsync(DomainCustomer customer, CancellationToken ct) => Task.CompletedTask;

        public Task<Guid?> FindIdByOriginAsync(Guid t, string s, Guid r, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid t,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(Guid t, string b, Guid? e, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid t,
            Guid p,
            int b,
            Guid a,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);
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
