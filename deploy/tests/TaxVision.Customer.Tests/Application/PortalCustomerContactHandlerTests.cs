using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Customer.Application.Customers.Portal;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;
using InMemoryCustomers = TaxVision.Customer.Tests.Application.CustomerProfileUpdateFromFormRequestedConsumerTests.InMemoryCustomers;

namespace TaxVision.Customer.Tests.Application;

// Autoservicio del portal: el cliente lee y cambia el teléfono de SU ficha (la que ve el preparador).
public sealed class PortalCustomerContactHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PortalUser = Guid.NewGuid();

    [Fact]
    public async Task Reads_the_phone_and_email_of_the_customer_record()
    {
        var customer = NewCustomer();
        customer.ChangePrimaryPhone(PhoneNumber.Create("+18095551234").Value, PortalUser);
        var repo = Repo(customer);

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(TenantId, customer.Id),
            repo,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("+18095551234", result.Value.PrimaryPhone);
        Assert.Equal("ana@example.com", result.Value.PrimaryEmail);
    }

    [Fact]
    public async Task Another_tenant_reads_as_not_found()
    {
        var customer = NewCustomer();

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(Guid.NewGuid(), customer.Id),
            Repo(customer),
            CancellationToken.None
        );

        Assert.Equal("Customer.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Saves_the_phone_in_E164_and_publishes_customer_updated()
    {
        var customer = NewCustomer();
        var bus = new FakeMessageBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateAsync(customer, "+1 (809) 555-1234", bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal("+18095551234", customer.PrimaryPhone?.E164Value);
        Assert.Equal(1, unitOfWork.Saves);
        var updated = Assert.IsType<CustomerUpdatedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("+18095551234", updated.PrimaryPhone);
        Assert.Equal(PortalUser, updated.ModifiedByUserId);
    }

    [Fact]
    public async Task Empty_value_removes_the_phone()
    {
        var customer = NewCustomer();
        customer.ChangePrimaryPhone(PhoneNumber.Create("+18095551234").Value, PortalUser);

        var result = await UpdateAsync(customer, "", new FakeMessageBus(), new CountingUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.Null(customer.PrimaryPhone);
    }

    [Fact]
    public async Task Invalid_phone_is_rejected_without_saving()
    {
        var customer = NewCustomer();
        var bus = new FakeMessageBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateAsync(customer, "809-555", bus, unitOfWork);

        Assert.Equal("Phone.Format", result.Error.Code);
        Assert.Null(customer.PrimaryPhone);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Same_phone_is_a_no_op()
    {
        var customer = NewCustomer();
        customer.ChangePrimaryPhone(PhoneNumber.Create("+18095551234").Value, PortalUser);
        var bus = new FakeMessageBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await UpdateAsync(customer, "+1 809 555 1234", bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Archived_customer_cannot_be_changed()
    {
        var customer = NewCustomer();
        customer.Archive(PortalUser);

        var result = await UpdateAsync(customer, "+18095551234", new FakeMessageBus(), new CountingUnitOfWork());

        Assert.Equal("Customer.NotFound", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result> UpdateAsync(
        DomainCustomer customer,
        string? phone,
        FakeMessageBus bus,
        CountingUnitOfWork unitOfWork
    ) =>
        UpdateMyCustomerPhoneHandler.Handle(
            new UpdateMyCustomerPhoneCommand(TenantId, customer.Id, PortalUser, phone),
            Repo(customer),
            bus,
            new FixedCorrelation(),
            unitOfWork,
            CancellationToken.None
        );

    private static InMemoryCustomers Repo(DomainCustomer customer)
    {
        var repo = new InMemoryCustomers();
        repo.All.Add(customer);
        return repo;
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
                PortalUser
            )
            .Value;

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
