using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Customers.Avatars;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class CustomerAvatarScanResultConsumerTests
{
    private static readonly Guid Uploader = Guid.NewGuid();

    [Fact]
    public async Task Available_pending_picture_becomes_current_deletes_the_previous_and_announces_it()
    {
        var customer = WithPicture(out var previous);
        var pending = Begin(customer);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await AvailableAsync(customer, pending, bus, unitOfWork);

        Assert.Equal(pending, customer.ProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);

        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(previous, Assert.Single(deletion.Files).FileId);
        Assert.Equal("avatar_replaced", deletion.Reason);

        var updated = Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Equal(pending, updated.AvatarFileId);
        Assert.Equal(Uploader, updated.ModifiedByUserId);
        Assert.Equal(customer.Id, updated.CustomerId);
    }

    [Fact]
    public async Task First_picture_is_confirmed_without_any_deletion()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var bus = new FakeMessageBus();

        await AvailableAsync(customer, pending, bus, new RecordingUnitOfWork(bus));

        Assert.Equal(pending, customer.ProfilePictureFileId);
        Assert.Empty(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task Redelivery_of_the_current_picture_is_a_no_op()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);
        await AvailableAsync(customer, pending, bus, unitOfWork);
        bus.Published.Clear();

        await AvailableAsync(customer, pending, bus, unitOfWork);

        Assert.Equal(pending, customer.ProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Stale_picture_is_not_confirmed_and_its_deletion_is_requested()
    {
        var customer = WithPicture(out var current);
        var pending = Begin(customer);
        var stale = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await AvailableAsync(customer, stale, bus, unitOfWork);

        Assert.Equal(current, customer.ProfilePictureFileId);
        Assert.Equal(pending, customer.PendingProfilePictureFileId);
        Assert.Equal(0, unitOfWork.Saves);
        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(stale, Assert.Single(deletion.Files).FileId);
        Assert.Equal("avatar_orphaned", deletion.Reason);
    }

    [Fact]
    public async Task Picture_arriving_after_the_customer_was_archived_is_deleted()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        customer.Archive(Uploader);
        var bus = new FakeMessageBus();

        await AvailableAsync(customer, pending, bus, new RecordingUnitOfWork(bus));

        Assert.Null(customer.ProfilePictureFileId);
        var deletion = Assert.IsType<FilesDeletionRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(pending, Assert.Single(deletion.Files).FileId);
    }

    [Theory]
    [InlineData("Tenant", "Avatars")]
    [InlineData("Customer", "Documents")]
    [InlineData(null, null)]
    public async Task Files_that_are_not_customer_avatars_are_ignored(string? ownerType, string? folderType)
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await CustomerAvatarScanResultConsumer.Handle(
            Available(customer, pending) with
            {
                OwnerType = ownerType,
                FolderType = folderType,
            },
            Repo(customer),
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        Assert.Equal(pending, customer.PendingProfilePictureFileId);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Event_from_another_tenant_is_ignored()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var bus = new FakeMessageBus();

        await CustomerAvatarScanResultConsumer.Handle(
            Available(customer, pending) with
            {
                TenantId = Guid.NewGuid(),
            },
            Repo(customer),
            new RecordingUnitOfWork(bus),
            bus,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        Assert.Equal(pending, customer.PendingProfilePictureFileId);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Infected_pending_picture_is_discarded_and_the_current_one_stays()
    {
        var customer = WithPicture(out var current);
        var pending = Begin(customer);
        var unitOfWork = new RecordingUnitOfWork(new FakeMessageBus());

        await CustomerAvatarScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = pending,
                ObjectKey = "k",
                ScanReport = "Eicar",
                OwnerType = "Customer",
                OwnerId = customer.Id,
                FolderType = "Avatars",
            },
            Repo(customer),
            unitOfWork,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        AssertDiscarded(customer, current, unitOfWork);
    }

    [Fact]
    public async Task Pending_picture_blocked_by_policy_is_discarded()
    {
        var customer = WithPicture(out var current);
        var pending = Begin(customer);
        var unitOfWork = new RecordingUnitOfWork(new FakeMessageBus());

        await CustomerAvatarScanResultConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = TenantId,
                FileId = pending,
                ObjectKey = "k",
                PolicyReason = "nsfw",
                CreatedBy = Uploader,
                OwnerType = "Customer",
                OwnerId = customer.Id,
                FolderType = "Avatars",
            },
            Repo(customer),
            unitOfWork,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        AssertDiscarded(customer, current, unitOfWork);
    }

    [Fact]
    public async Task Pending_picture_whose_scan_failed_is_discarded()
    {
        var customer = WithPicture(out var current);
        var pending = Begin(customer);
        var unitOfWork = new RecordingUnitOfWork(new FakeMessageBus());

        await CustomerAvatarScanResultConsumer.Handle(
            ScanFailed(customer, pending),
            Repo(customer),
            unitOfWork,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        AssertDiscarded(customer, current, unitOfWork);
    }

    [Fact]
    public async Task Failure_of_a_file_that_is_not_pending_changes_nothing()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var unitOfWork = new RecordingUnitOfWork(new FakeMessageBus());

        await CustomerAvatarScanResultConsumer.Handle(
            ScanFailed(customer, Guid.NewGuid()),
            Repo(customer),
            unitOfWork,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

        Assert.Equal(pending, customer.PendingProfilePictureFileId);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Repeated_failure_is_idempotent()
    {
        var customer = NewCustomer();
        var pending = Begin(customer);
        var unitOfWork = new RecordingUnitOfWork(new FakeMessageBus());
        var repo = Repo(customer);

        for (var i = 0; i < 2; i++)
            await CustomerAvatarScanResultConsumer.Handle(
                ScanFailed(customer, pending),
                repo,
                unitOfWork,
                new FixedCorrelationContext(),
                NullLogger<DomainCustomer>.Instance,
                CancellationToken.None
            );

        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    private static void AssertDiscarded(DomainCustomer customer, Guid current, RecordingUnitOfWork unitOfWork)
    {
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureRequestedAtUtc);
        Assert.Equal(current, customer.ProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    private static Guid Begin(DomainCustomer customer)
    {
        var fileId = Guid.NewGuid();
        customer.BeginProfilePictureChange(fileId, Uploader, Now);
        return fileId;
    }

    private static Task AvailableAsync(
        DomainCustomer customer,
        Guid fileId,
        FakeMessageBus bus,
        RecordingUnitOfWork unitOfWork
    ) =>
        CustomerAvatarScanResultConsumer.Handle(
            Available(customer, fileId),
            Repo(customer),
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            NullLogger<DomainCustomer>.Instance,
            CancellationToken.None
        );

    private static FileAvailableIntegrationEvent Available(DomainCustomer customer, Guid fileId) =>
        new()
        {
            TenantId = TenantId,
            FileId = fileId,
            ObjectKey = $"tenants/{TenantId}/customer/{customer.Id}/avatars/{fileId}.webp",
            ContentType = "image/webp",
            SizeBytes = 42,
            ChecksumSha256 = "abc",
            CreatedBy = Uploader,
            OwnerType = "Customer",
            OwnerId = customer.Id,
            FolderType = "Avatars",
        };

    private static FileScanFailedIntegrationEvent ScanFailed(DomainCustomer customer, Guid fileId) =>
        new()
        {
            TenantId = TenantId,
            FileId = fileId,
            ObjectKey = "k",
            Reason = "clamav unreachable",
            OwnerType = "Customer",
            OwnerId = customer.Id,
            FolderType = "Avatars",
        };
}
