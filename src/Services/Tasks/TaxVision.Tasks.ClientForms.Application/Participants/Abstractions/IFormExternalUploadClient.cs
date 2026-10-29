using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public sealed record ExternalFormUploadRequest(
    Guid TenantId,
    string OwnerType,
    Guid OwnerId,
    string OriginalName,
    string ContentType,
    long SizeBytes
);

public sealed record ExternalFormUploadResponse(
    Guid FileId,
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> FormData,
    DateTime ExpiresAtUtc
);

public interface IFormExternalUploadClient
{
    Task<Result<ExternalFormUploadResponse>> InitiateAsync(
        ExternalFormUploadRequest request,
        CancellationToken ct = default
    );

    Task<Result> CompleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
