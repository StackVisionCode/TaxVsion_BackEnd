using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Customers.Avatars;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class RemoveCustomerAvatarHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Removing_the_picture_requests_its_deletion_and_announces_the_change()
    {
        var customer = WithPicture(out var current);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        var result = await RunAsync(customer, CustomerAvatarCaller.PortalSelf, bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Null(customer.ProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);

        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        var target = Assert.Single(deletion.Files);
        Assert.Equal(current, target.FileId);
        Assert.Equal("Customer", target.ExpectedOwnerType);
        Assert.Equal(customer.Id, target.ExpectedOwnerId);
        Assert.Equal("Avatars", target.ExpectedFolderType);
        Assert.Equal("avatar_removed", deletion.Reason);
        Assert.Equal(customer.Id, deletion.SourceReferenceId);

        var updated = Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Null(updated.AvatarFileId);
        Assert.Equal(Actor, updated.ModifiedByUserId);
    }

    [Fact]
    public async Task Removing_without_picture_is_idempotent()
    {
        var customer = NewCustomer();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        var result = await RunAsync(customer, CustomerAvatarCaller.PortalSelf, bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Removing_only_a_pending_upload_saves_but_publishes_nothing()
    {
        var customer = NewCustomer();
        customer.BeginProfilePictureChange(Guid.NewGuid(), Actor, Now);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        var result = await RunAsync(customer, CustomerAvatarCaller.PortalSelf, bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Unassigned_staff_sees_not_found_and_the_picture_stays()
    {
        var customer = WithPicture(out var current);
        var bus = new FakeMessageBus();

        var result = await RunAsync(customer, CustomerAvatarCaller.Staff, bus, new RecordingUnitOfWork(bus));

        Assert.Equal("Customer.NotFound", result.Error.Code);
        Assert.Equal(current, customer.ProfilePictureFileId);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Assigned_staff_can_remove()
    {
        var customer = WithPicture(out _);
        customer.AssignPreparer(Actor, Actor);
        var bus = new FakeMessageBus();

        var result = await RunAsync(customer, CustomerAvatarCaller.Staff, bus, new RecordingUnitOfWork(bus));

        Assert.True(result.IsSuccess);
        Assert.Null(customer.ProfilePictureFileId);
    }

    [Fact]
    public async Task Archived_customer_is_rejected_for_staff()
    {
        var customer = NewCustomer();
        customer.Archive(Actor);
        var bus = new FakeMessageBus();

        var result = await RunAsync(
            customer,
            CustomerAvatarCaller.Staff,
            bus,
            new RecordingUnitOfWork(bus),
            canViewAll: true
        );

        Assert.Equal("Customer.Archived", result.Error.Code);
    }

    private static Task<Result> RunAsync(
        DomainCustomer customer,
        CustomerAvatarCaller caller,
        FakeMessageBus bus,
        RecordingUnitOfWork unitOfWork,
        bool canViewAll = false
    ) =>
        RemoveCustomerAvatarHandler.Handle(
            new RemoveCustomerAvatarCommand(TenantId, customer.Id, Actor, caller, canViewAll),
            Repo(customer),
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            CancellationToken.None
        );
}
