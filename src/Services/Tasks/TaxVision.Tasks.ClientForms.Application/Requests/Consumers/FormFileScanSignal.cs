using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Consumers;

// Lo común de los cinco eventos de CloudStorage; lo que un evento no trae llega en null.
internal sealed record FormFileScanSignal(
    Guid TenantId,
    Guid FileId,
    FormFileVerdictStatus Status,
    string? OwnerType,
    Guid? OwnerId,
    string? FolderType,
    string? ContentType,
    long? SizeBytes,
    string? OriginalName,
    DateTime OccurredAtUtc
);
