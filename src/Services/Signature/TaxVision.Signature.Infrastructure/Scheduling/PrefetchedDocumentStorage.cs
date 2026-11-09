using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// SIG-BB3: CloudStorage dentro de la transacción de un job. Sirve solo lo que el job ya bajó antes de abrir la
/// unidad; lo que falta queda como descarga fallida (la verificación lo difiere) y nunca sale a la red.
/// </summary>
internal sealed class PrefetchedDocumentStorage(IReadOnlyDictionary<Guid, byte[]> downloads)
    : ISignatureCloudStorageClient
{
    public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(
            downloads.TryGetValue(fileId, out var bytes)
                ? Result.Success(bytes)
                : Result.Failure<byte[]>(
                    new Error("Signature.Storage.NotPrefetched", "The file was not downloaded before the job unit.")
                )
        );

    public Task<Result<SaveFileRequestedIntegrationEvent>> StageAsync(
        Guid tenantId,
        SignatureFileUpload upload,
        CancellationToken ct = default
    ) => throw new NotSupportedException("CloudStorage is not called inside a job unit.");

    public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        throw new NotSupportedException("CloudStorage is not called inside a job unit.");

    public Task<Result<Guid>> CopyToCustomerAsync(
        Guid tenantId,
        Guid sourceFileId,
        Guid customerId,
        int taxYear,
        CancellationToken ct = default
    ) => throw new NotSupportedException("CloudStorage is not called inside a job unit.");

    public Task<Result<SignatureFileMetadata>> GetFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    ) => throw new NotSupportedException("CloudStorage is not called inside a job unit.");
}
