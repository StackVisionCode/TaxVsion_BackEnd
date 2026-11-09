using BuildingBlocks.Common;
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
/// CUS-6: caracteriza cómo el alta arma el cliente desde el comando, y que un dato inválido corta antes del candado.
/// </summary>
public sealed class CreateCustomerHandlerDraftTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Theory]
    [InlineData("not-an-email", null, "Email.Invalid")]
    [InlineData("ok@example.com", "123", "Phone.Format")]
    public async Task A_bad_contact_stops_before_taking_the_lock(string email, string? phone, string code)
    {
        var world = new World();

        var result = await world.CreateAsync(Person(email, phone));

        Assert.Equal(code, result.Error.Code);
        Assert.Empty(world.Steps);
        Assert.Empty(world.Customers.All);
    }

    [Fact]
    public async Task An_individual_without_a_name_is_refused()
    {
        var world = new World();

        var result = await world.CreateAsync(Person("ok@example.com") with { FirstName = null });

        Assert.StartsWith("PersonalName", result.Error.Code);
        Assert.Empty(world.Steps);
    }

    [Fact]
    public async Task A_business_needs_its_structure()
    {
        var world = new World();

        var result = await world.CreateAsync(Company() with { BusinessStructure = null });

        Assert.Equal("Customer.BusinessStructure", result.Error.Code);
        Assert.Equal("Business structure is required.", result.Error.Message);
    }

    [Fact]
    public async Task A_business_without_a_legal_name_is_refused()
    {
        var world = new World();

        var result = await world.CreateAsync(Company() with { LegalName = null });

        Assert.StartsWith("BusinessIdentity", result.Error.Code);
    }

    [Fact]
    public async Task An_individual_ignores_the_business_fields()
    {
        var world = new World();

        var result = await world.CreateAsync(
            Person("ok@example.com") with
            {
                LegalName = "Ignored",
                BusinessStructure = null,
            }
        );

        Assert.True(result.IsSuccess);
        var customer = Assert.Single(world.Customers.All);
        Assert.Equal("Ada Lovelace", customer.DisplayName);
        Assert.Null(customer.BusinessIdentity);
    }

    [Fact]
    public async Task A_business_ignores_the_personal_fields_and_keeps_its_contact_policy()
    {
        var world = new World();

        var result = await world.CreateAsync(
            Company() with
            {
                FirstName = "Ignored",
                PrimaryPhone = "+13055550100",
                EmailEnabled = false,
                SmsEnabled = true,
            }
        );

        Assert.True(result.IsSuccess);
        var customer = Assert.Single(world.Customers.All);
        Assert.Equal("Acme LLC", customer.DisplayName);
        Assert.Equal("Acme LLC", customer.BusinessIdentity!.LegalName);
        Assert.Null(customer.PersonalName);
        Assert.Equal("+13055550100", customer.PrimaryPhone!.E164Value);
        Assert.False(customer.ContactPolicy.EmailEnabled);
        Assert.True(customer.ContactPolicy.SmsEnabled);
    }

    [Fact]
    public async Task The_duplicate_lookup_compares_the_name_the_customer_would_get()
    {
        var person = new World();
        await person.CreateAsync(Person("ok@example.com"));
        var company = new World();
        await company.CreateAsync(Company());

        Assert.Equal("Ada Lovelace", person.LastCandidate!.DisplayName);
        Assert.Equal("Acme LLC", company.LastCandidate!.DisplayName);
        Assert.Equal(["lock:ok@example.com", "detect"], person.Steps);
    }

    [Fact]
    public async Task Overwriting_applies_the_name_only_to_the_same_kind_of_customer()
    {
        var world = new World();
        var existing = DomainCustomer
            .Register(
                Tenant,
                CustomerKind.Business,
                null,
                BusinessIdentity.Create("Old LLC", BusinessStructure.Llc, null, null, null).Value,
                EmailAddress.Create("old@example.com").Value,
                null,
                Language.En,
                User
            )
            .Value;
        world.Customers.All.Add(existing);
        world.Match = new DuplicateMatch(0, existing.Id, existing.DisplayName, "email");

        var result = await world.CreateAsync(Person("new@example.com") with { Overwrite = true });

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.com", existing.PrimaryEmail.Value);
        Assert.Equal("Old LLC", existing.DisplayName);
        Assert.Single(world.Customers.All);
    }

    private static CreateCustomerCommand Person(string email, string? phone = null) =>
        new(
            Tenant,
            User,
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
            email,
            phone,
            Language.En
        );

    private static CreateCustomerCommand Company() =>
        new(
            Tenant,
            User,
            CustomerKind.Business,
            null,
            null,
            null,
            null,
            null,
            "Acme LLC",
            BusinessStructure.Llc,
            null,
            null,
            null,
            null,
            null,
            "acme@example.com",
            null,
            Language.En
        );

    private sealed class World : ICustomerDuplicateDetector, ICustomerCreationLock, IUnitOfWork
    {
        public CustomerCommandEventCountTests.AllCustomers Customers { get; } = new([]);
        public List<string> Steps { get; } = [];
        public CustomerDuplicateCandidate? LastCandidate { get; private set; }
        public DuplicateMatch? Match { get; set; }

        public Task<Result<CustomerResponse>> CreateAsync(CreateCustomerCommand command) =>
            CreateCustomerHandler.Handle(
                command,
                Customers,
                this,
                new NoDirectory(),
                this,
                this,
                new FakeMessageBus(),
                new FixedCorrelation(),
                CancellationToken.None
            );

        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
        {
            Steps.Add($"lock:{normalizedEmail}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        )
        {
            Steps.Add("detect");
            LastCandidate = candidate;
            return Task.FromResult(Match);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class NoDirectory : ITenantEmployeeDirectoryRepository
    {
        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<TenantEmployeeDirectoryEntry?>(null);

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

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr-create";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => throw new NotSupportedException();
    }
}
