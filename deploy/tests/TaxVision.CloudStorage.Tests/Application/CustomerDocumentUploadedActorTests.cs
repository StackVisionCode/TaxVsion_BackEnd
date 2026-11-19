using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Application.Files.DomainEvents;
using TaxVision.CloudStorage.Domain.Files.Events;

namespace TaxVision.CloudStorage.Tests.Application;

// Work Profile: el aviso de documento del cliente dice quién lo subió y con qué tipo de actor.
public sealed class CustomerDocumentUploadedActorTests
{
    [Fact]
    public void Portal_upload_carries_the_portal_user_and_its_actor_type()
    {
        var uploader = Guid.NewGuid();

        var uploaded = Assert.Single(Publish(uploader, portalCustomerId: Guid.NewGuid()));

        Assert.Equal(uploader, uploaded.UploadedByUserId);
        Assert.Equal(nameof(ActorType.CustomerPortal), uploaded.UploadedByActorType);
    }

    [Fact]
    public void Staff_upload_does_not_announce_a_customer_document()
    {
        Assert.Empty(Publish(Guid.NewGuid(), portalCustomerId: null));
    }

    [Fact]
    public void Upload_without_a_user_leaves_the_actor_empty()
    {
        var uploaded = Assert.Single(Publish(Guid.Empty, portalCustomerId: Guid.NewGuid()));

        Assert.Null(uploaded.UploadedByUserId);
        Assert.Null(uploaded.UploadedByActorType);
    }

    private static List<CustomerDocumentUploadedIntegrationEvent> Publish(Guid createdBy, Guid? portalCustomerId) =>
        FileBecameAvailableDomainEventHandler
            .Handle(
                new FileBecameAvailableDomainEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "tenant/customer/file.pdf",
                    "application/pdf",
                    10,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    createdBy,
                    "Customer",
                    Guid.NewGuid(),
                    null,
                    "Documents",
                    "file.pdf",
                    2026,
                    "corr",
                    portalCustomerId
                )
            )
            .OfType<CustomerDocumentUploadedIntegrationEvent>()
            .ToList();
}
