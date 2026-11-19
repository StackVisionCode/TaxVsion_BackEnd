using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Documento de cliente subido por staff de la oficina; lo que sube el cliente desde el portal no cuenta.</summary>
public static class CloudStorageActivityConsumer
{
    public static Task Handle(
        CustomerDocumentUploadedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(StaffActor(evt), ActivityVerbs.DocumentUploaded, evt.FileId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    private static Guid? StaffActor(CustomerDocumentUploadedIntegrationEvent evt) =>
        evt.UploadedByActorType is nameof(ActorType.TenantEmployee) or nameof(ActorType.TenantAdmin)
            ? evt.UploadedByUserId
            : null;
}
