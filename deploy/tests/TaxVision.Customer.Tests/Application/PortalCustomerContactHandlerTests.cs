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
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reads_the_phone_and_email_of_the_customer_record()
    {
        var customer = NewCustomer();
        customer.ChangePrimaryPhone(PhoneNumber.Create("+18095551234").Value, PortalUser);
        var repo = Repo(customer);

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(TenantId, customer.Id),
            repo,
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("+18095551234", result.Value.PrimaryPhone);
        Assert.Equal("ana@example.com", result.Value.PrimaryEmail);
    }

    [Fact]
    public async Task Contact_includes_the_current_picture_and_the_upload_in_progress()
    {
        var customer = NewCustomer();
        var current = Guid.NewGuid();
        customer.BeginProfilePictureChange(current, PortalUser, Now);
        customer.ConfirmProfilePicture(current);
        var pending = Guid.NewGuid();
        customer.BeginProfilePictureChange(pending, PortalUser, Now);

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(TenantId, customer.Id),
            Repo(customer),
            new FixedTimeProvider(Now.AddMinutes(1)),
            CancellationToken.None
        );

        Assert.Equal(current, result.Value.AvatarFileId);
        Assert.Equal(pending, result.Value.PendingAvatarFileId);
        Assert.Equal("processing", result.Value.AvatarUploadStatus);
    }

    [Fact]
    public async Task Pending_picture_older_than_the_timeout_reads_as_failed()
    {
        var customer = NewCustomer();
        var pending = Guid.NewGuid();
        customer.BeginProfilePictureChange(pending, PortalUser, Now);

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(TenantId, customer.Id),
            Repo(customer),
            new FixedTimeProvider(Now.AddMinutes(11)),
            CancellationToken.None
        );

        Assert.Null(result.Value.AvatarFileId);
        Assert.Equal(pending, result.Value.PendingAvatarFileId);
        Assert.Equal("failed", result.Value.AvatarUploadStatus);
    }

    [Fact]
    public async Task Without_picture_nor_upload_the_avatar_fields_are_empty()
    {
        var customer = NewCustomer();

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(TenantId, customer.Id),
            Repo(customer),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Null(result.Value.AvatarFileId);
        Assert.Null(result.Value.PendingAvatarFileId);
        Assert.Null(result.Value.AvatarUploadStatus);
    }

    [Fact]
    public async Task Another_tenant_reads_as_not_found()
    {
        var customer = NewCustomer();

        var result = await GetMyCustomerContactHandler.Handle(
            new GetMyCustomerContactQuery(Guid.NewGuid(), customer.Id),
            Repo(customer),
            TimeProvider.System,
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
