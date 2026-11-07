using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Gate anti-carrera con ClamAV: decide qué imágenes de firma se pueden embeber en el sello.</summary>
public sealed class SignatureImageScanGate(
    IFileMetadataRefRepository fileRefRepository,
    ISignatureCloudStorageClient storage,
    ILogger<SignatureRequest> logger
)
{
    /// <summary>Código del fallo definitivo por imagen de firma inexistente o con escaneo fallido.</summary>
    public const string SignatureImageMissingCode = "Signature.Sealing.SignatureImageMissing";

    /// <summary>
    /// Tope de espera del scan de la imagen de firma antes de sellar con fallback tipográfico. Supera la
    /// suma de cooldowns del RetryWithCooldown dedicado (~2 min) para dar una última oportunidad al scan;
    /// pasado esto, la entrega NUNCA se bloquea (se degrada el sello, no se pierde el envelope).
    /// </summary>
    private static readonly TimeSpan MaxSignatureImageScanWait = TimeSpan.FromMinutes(2);

    // El 99% de las "carreras" entre SignatureRequestCompleted y FileAvailable son de ~500 ms:
    // el `/sign` publica Completed inmediatamente, el PNG ya está en MinIO y Clam escanea en
    // paralelo. Antes de rendirnos y pedirle a Wolverine un redelivery (que ensucia el log con
    // Error y dobla el handler), aguantamos aquí mismo con un poll corto: refresca la proyección
    // cada 300 ms hasta 5 s. Si en ese plazo el FileAvailable propagó, el consumer sigue sin
    // romper nada; si no, tira la excepción y el redelivery de Wolverine actúa como lo hacía.
    private static readonly TimeSpan InProcessScanPollBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InProcessScanPollInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Recorre los firmantes con imagen de firma y consulta la proyección local de readiness
    /// (<see cref="FileMetadataRef"/>, alimentada por FileAvailable/FileInfected de CloudStorage):
    /// <list type="bullet">
    ///   <item><description>Ausente o <c>Pending</c> → aún escaneando: lanza
    ///     <see cref="SignatureImageNotReadyException"/> para que Wolverine reintente el sellado. Si CloudStorage
    ///     no la tiene (404) o su escaneo falló, devuelve <see cref="SignatureImageMissingCode"/>.</description></item>
    ///   <item><description><c>Available</c> → el id del firmante entra al set descargable.</description></item>
    ///   <item><description><c>Infected</c>/<c>Deleted</c>/<c>Rejected</c> → se excluye (nunca se embebe): ese campo
    ///     cae al sello tipográfico. No se reintenta porque no va a mejorar.</description></item>
    /// </list>
    /// </summary>
    public async Task<Result<IReadOnlySet<Guid>>> ResolveAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        CancellationToken ct
    )
    {
        var ready = new HashSet<Guid>();
        var requested = requestedDocumentIds.ToHashSet();
        foreach (
            var signer in request.Signers.Where(signer =>
                signer.Fields.Any(field => requested.Contains(field.DocumentId))
            )
        )
        {
            if (signer.SignatureImageFileId is not { } imageFileId)
                continue;

            var projection = await WaitForScanOutcomeAsync(request.TenantId, imageFileId, ct);
            switch (projection?.Status)
            {
                case FileScanStatus.Available:
                    ready.Add(signer.Id);
                    break;

                case FileScanStatus.Infected:
                case FileScanStatus.Deleted:
                case FileScanStatus.Rejected:
                    logger.LogWarning(
                        "Signature image {FileId} for signer {SignerId} is {Status}; sealing with typographic fallback.",
                        imageFileId,
                        signer.Id,
                        projection.Status
                    );
                    break;

                default:
                    // Proyección ausente/Pending: CloudStorage dice si sigue escaneando o si no existe.
                    var remote = await storage.GetFileAsync(request.TenantId, imageFileId, ct);
                    if (IsSignatureImageMissing(remote))
                    {
                        logger.LogWarning(
                            "Signature image {FileId} for signer {SignerId} does not exist or failed scanning in CloudStorage; sealing fails.",
                            imageFileId,
                            signer.Id
                        );
                        return Result.Failure<IReadOnlySet<Guid>>(
                            new Error(
                                SignatureImageMissingCode,
                                $"The signature image of signer {signer.Id} is missing in storage; the signer must sign again."
                            )
                        );
                    }

                    if (
                        remote.IsSuccess
                        && string.Equals(remote.Value.Status, "Available", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        ready.Add(signer.Id);
                        break;
                    }

                    // Aún escaneando (proyección ausente/Pending). Reintentar da tiempo al scan, PERO con
                    // un tope: si tras MaxScanWait el archivo sigue sin estar Available (scan roto/atascado,
                    // o la subida nunca se registró), NO se bloquea la entrega para siempre — se sella con
                    // fallback tipográfico y se emite el documento. Mejor un sello degradado entregado que
                    // un envelope que nunca llega al firmante.
                    var elapsed = DateTime.UtcNow - evidenceAtUtc;
                    if (elapsed > MaxSignatureImageScanWait)
                    {
                        logger.LogWarning(
                            "Signature image {FileId} for signer {SignerId} is still not Available after {Elapsed}; "
                                + "sealing with typographic fallback to avoid blocking delivery.",
                            imageFileId,
                            signer.Id,
                            elapsed
                        );
                        break;
                    }

                    throw new SignatureImageNotReadyException(
                        $"Signature image {imageFileId} for signer {signer.Id} has not finished virus scanning yet."
                    );
            }
        }

        return Result.Success<IReadOnlySet<Guid>>(ready);
    }

    /// <summary>
    /// Resuelve la proyección de scan absorbiendo la carrera corta entre `SignatureRequestCompleted`
    /// (publicado al firmar) y `FileAvailable` (publicado por el escáner de CloudStorage). Si al primer
    /// vistazo la proyección sigue en Pending/ausente, hace poll in-process cada
    /// <see cref="InProcessScanPollInterval"/> hasta agotar <see cref="InProcessScanPollBudget"/>. Al
    /// terminar devuelve el último snapshot — el llamador decide qué hacer con un Pending residual.
    /// </summary>
    private async Task<FileMetadataRef?> WaitForScanOutcomeAsync(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        var projection = await fileRefRepository.GetByFileIdAsync(tenantId, fileId, ct);
        if (projection is { HasScanOutcome: true })
            return projection;

        var deadline = DateTime.UtcNow + InProcessScanPollBudget;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(InProcessScanPollInterval, ct);
            projection = await fileRefRepository.GetByFileIdAsync(tenantId, fileId, ct);
            if (projection is { HasScanOutcome: true })
                return projection;
        }

        return projection;
    }

    // Permanente: 404 o escaneo fallido. Un error de red o un estado de escaneo sigue siendo transitorio.
    private static bool IsSignatureImageMissing(Result<SignatureFileMetadata> remote) =>
        remote.IsFailure
            ? remote.Error.Code == SignatureStorage.FileNotFoundCode
            : string.Equals(remote.Value.Status, "ScanFailed", StringComparison.OrdinalIgnoreCase);
}
