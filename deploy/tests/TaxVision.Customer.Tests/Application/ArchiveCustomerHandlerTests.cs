using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Application.Customers.Commands.Archive;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;

namespace TaxVision.Customer.Tests.Application;

// Archivar retira la foto del cliente y pide su borrado en CloudStorage.
public sealed class ArchiveCustomerHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Archiving_clears_the_picture_and_requests_its_deletion_after_saving()
    {
        var customer = WithPicture(out var photo);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        var result = await ArchiveCustomerHandler.Handle(
            new ArchiveCustomerCommand(TenantId, customer.Id, Admin),
            Repo(customer),
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(customer.ProfilePictureFileId);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);
        Assert.Single(bus.Published.OfType<CustomerArchivedIntegrationEvent>());
        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        var target = Assert.Single(deletion.Files);
        Assert.Equal(photo, target.FileId);
        Assert.Equal(customer.Id, target.ExpectedOwnerId);
        Assert.Equal("customer_archived", deletion.Reason);
    }

    [Fact]
    public async Task Archiving_a_customer_without_picture_requests_no_deletion()
    {
        var customer = NewCustomer();
        var bus = new FakeMessageBus();

        await ArchiveCustomerHandler.Handle(
            new ArchiveCustomerCommand(TenantId, customer.Id, Admin),
            Repo(customer),
            new RecordingUnitOfWork(bus),
            bus,
            new FixedCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
    }
}
