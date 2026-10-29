using BuildingBlocks.Imaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Customers.Avatars;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class UploadCustomerAvatarHandlerTests
{
    private static readonly Guid PortalUser = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly byte[] RawUpload = [9, 9, 9];

    [Fact]
    public async Task Portal_upload_leaves_the_picture_pending_and_requests_the_catalog()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer);

        var result = await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.True(result.IsSuccess);
        Assert.Equal(CustomerAvatarStatus.Processing, result.Value.Status);
        Assert.Equal(result.Value.FileId, customer.PendingProfilePictureFileId);
        Assert.Equal(Now, customer.PendingProfilePictureRequestedAtUtc);
        Assert.Null(customer.ProfilePictureFileId);

        var stored = Assert.Single(harness.Storage.Stored);
        Assert.Equal(result.Value.FileId, stored.FileId);
        Assert.Equal(StubAvatarNormalizer.NormalizedBytes, stored.Content);
        Assert.Equal("image/webp", stored.ContentType);

        var save = Assert.IsType<SaveFileRequestedIntegrationEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal(result.Value.FileId, save.FileId);
        Assert.Equal(TenantId, save.TenantId);
        Assert.Equal("Customer", save.OwnerType);
        Assert.Equal(customer.Id, save.OwnerId);
        Assert.Equal("Avatars", save.FolderType);
        Assert.Equal("image/webp", save.ContentType);
        Assert.Equal("avatar.webp", save.OriginalName);
        Assert.Equal(StubAvatarNormalizer.NormalizedBytes.LongLength, save.SizeBytes);
        Assert.Equal(PortalUser, save.ActorId);
        Assert.Equal("taxvision-temp", save.SourceBucket);
    }

    [Fact]
    public async Task Pending_row_is_saved_before_the_catalog_request_is_published()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer);

        await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.Equal(1, harness.UnitOfWork.Saves);
        Assert.Equal(0, harness.UnitOfWork.PublishedAtFirstSave);
        Assert.Single(harness.Bus.Published);
    }

    [Fact]
    public async Task Staff_assigned_to_the_customer_can_upload()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Staff, Staff);
        var harness = new Harness(customer);

        var result = await harness.RunAsync(customer.Id, Staff, CustomerAvatarCaller.Staff);

        Assert.True(result.IsSuccess);
        Assert.Equal(Staff, Assert.IsType<SaveFileRequestedIntegrationEvent>(harness.Bus.Published[0]).ActorId);
    }

    [Fact]
    public async Task Staff_with_view_all_can_upload_to_an_unassigned_customer()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer);

        var result = await harness.RunAsync(customer.Id, Staff, CustomerAvatarCaller.Staff, canViewAll: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Unassigned_staff_sees_the_customer_as_not_found_and_nothing_is_processed()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer);

        var result = await harness.RunAsync(customer.Id, Staff, CustomerAvatarCaller.Staff);

        Assert.Equal("Customer.NotFound", result.Error.Code);
        harness.AssertNothingHappened(customer);
        Assert.Equal(0, harness.Normalizer.Calls);
    }

    [Fact]
    public async Task Customer_of_another_tenant_is_not_found()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer);

        var result = await harness.RunAsync(
            customer.Id,
            PortalUser,
            CustomerAvatarCaller.PortalSelf,
            tenantId: Guid.NewGuid()
        );

        Assert.Equal("Customer.NotFound", result.Error.Code);
        harness.AssertNothingHappened(customer);
    }

    [Fact]
    public async Task Archived_customer_is_rejected_for_staff_and_hidden_from_the_portal()
    {
        var customer = NewCustomer();
        customer.Archive(Staff);
        var harness = new Harness(customer);

        var staff = await harness.RunAsync(customer.Id, Staff, CustomerAvatarCaller.Staff, canViewAll: true);
        var portal = await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.Equal("Customer.Archived", staff.Error.Code);
        Assert.Equal("Customer.NotFound", portal.Error.Code);
        harness.AssertNothingHappened(customer);
    }

    [Fact]
    public async Task Invalid_image_returns_the_normalizer_error_without_storing()
    {
        var customer = NewCustomer();
        var harness = new Harness(
            customer,
            new StubAvatarNormalizer { FailWith = AvatarImageErrors.UnsupportedFormat }
        );

        var result = await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.Equal("Avatar.UnsupportedFormat", result.Error.Code);
        harness.AssertNothingHappened(customer);
    }

    [Fact]
    public async Task Storage_failure_is_reported_as_unavailable_and_nothing_is_saved()
    {
        var customer = NewCustomer();
        var harness = new Harness(customer, storage: new StubAvatarStorage { Fail = true });

        var result = await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.Equal("Customer.Avatar.StorageUnavailable", result.Error.Code);
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Equal(0, harness.UnitOfWork.Saves);
        Assert.Empty(harness.Bus.Published);
    }

    [Fact]
    public async Task A_new_upload_keeps_the_current_picture_until_the_new_one_is_confirmed()
    {
        var customer = WithPicture(out var current);
        var harness = new Harness(customer);

        var result = await harness.RunAsync(customer.Id, PortalUser, CustomerAvatarCaller.PortalSelf);

        Assert.Equal(current, customer.ProfilePictureFileId);
        Assert.Equal(result.Value.FileId, customer.PendingProfilePictureFileId);
    }

    private sealed class Harness
    {
        public Harness(
            DomainCustomer customer,
            StubAvatarNormalizer? normalizer = null,
            StubAvatarStorage? storage = null
        )
        {
            Customers = Repo(customer);
            Normalizer = normalizer ?? new StubAvatarNormalizer();
            Storage = storage ?? new StubAvatarStorage();
            UnitOfWork = new RecordingUnitOfWork(Bus);
        }

        public CustomerProfileUpdateFromFormRequestedConsumerTests.InMemoryCustomers Customers { get; }
        public StubAvatarNormalizer Normalizer { get; }
        public StubAvatarStorage Storage { get; }
        public FakeMessageBus Bus { get; } = new();
        public RecordingUnitOfWork UnitOfWork { get; }

        public Task<Result<UploadCustomerAvatarResponse>> RunAsync(
            Guid customerId,
            Guid actor,
            CustomerAvatarCaller caller,
            bool canViewAll = false,
            Guid? tenantId = null
        ) =>
            UploadCustomerAvatarHandler.Handle(
                new UploadCustomerAvatarCommand(tenantId ?? TenantId, customerId, actor, caller, canViewAll, RawUpload),
                Customers,
                Normalizer,
                Storage,
                UnitOfWork,
                Bus,
                new FixedCorrelationContext(),
                new FixedTimeProvider(Now),
                CancellationToken.None
            );

        public void AssertNothingHappened(DomainCustomer customer)
        {
            Assert.Null(customer.PendingProfilePictureFileId);
            Assert.Empty(Storage.Stored);
            Assert.Equal(0, UnitOfWork.Saves);
            Assert.Empty(Bus.Published);
        }
    }
}
