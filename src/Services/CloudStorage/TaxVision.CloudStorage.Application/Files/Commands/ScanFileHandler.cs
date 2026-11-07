using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.CloudStorage.Application.Files.Commands;

public sealed record ScanFileCommand(Guid TenantId, Guid FileId, string CorrelationId, Guid? PortalCustomerId = null);

/// <summary>
/// Pasa el archivo por el inspector, el antivirus y la moderación sin transacción abierta (fila L): reclama el
/// turno, deja el archivo en Scanning en una transacción corta, escanea y registra el veredicto en otra
/// (<see cref="RecordScanVerdictHandler"/>). El turno se sostiene hasta que el veredicto confirma.
/// </summary>
public static class ScanFileHandler
{
    private const string ContentNotAllowedReport = "Detected content type is not allowed.";
    private const string ModerationWithoutDetailReport = "Content policy verdict without detail.";

    [NonTransactional]
    public static async Task Handle(
        ScanFileCommand command,
        IScanClaims claims,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IObjectStorage storage,
        IVirusScanner virusScanner,
        IContentScanner contentScanner,
        IFileContentInspector inspector,
        IOptions<CloudStorageOptions> options,
        IUploadPolicyResolver uploadPolicies,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<ScanFileCommand> logger,
        CancellationToken ct
    )
    {
        // Antes de leer: así el que entra después de un escaneo terminado ya ve el estado final.
        await using var claim = await claims.TryClaimAsync(command.FileId, ct);
        if (claim is null)
        {
            logger.LogInformation("Scan skipped: file {FileId} is already being scanned.", command.FileId);
            return;
        }

        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
        {
            // Purgado mientras esperaba en cola: no va a aparecer, reintentar no sirve.
            logger.LogWarning("Scan skipped: file {FileId} no longer exists.", command.FileId);
            return;
        }

        // Terminales — un redelivery de ScanFileCommand no debe relanzar el scan. ScanFailed ya es
        // definitivo (su reserva se liberó); el reescaneo pasa por RescanFileCommand.
        if (
            file.Status
            is FileStatus.Available
                or FileStatus.Infected
                or FileStatus.BlockedByPolicy
                or FileStatus.PendingReview
                or FileStatus.ScanFailed
                or FileStatus.SoftDeleted
        )
            return;

        // Transacción corta: el reclamo queda visible y un borrado posterior gana al veredicto.
        var transition = file.MarkScanning();
        if (transition.IsFailure)
            throw new InvalidOperationException(transition.Error.Message);
        await unitOfWork.SaveChangesAsync(ct);

        var sourceBucket = await LocateStoredObjectAsync(storage, options.Value, file.ObjectKey, ct);
        if (sourceBucket is null)
        {
            await bus.InvokeAsync(RecordScanVerdictCommand.BlobMissing(command), ct);
            logger.LogWarning("Scan of file {FileId} found no stored object; marked blob_missing.", file.Id);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"taxvision-scan-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (
                var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            {
                await storage.DownloadAsync(sourceBucket, file.ObjectKey, destination, ct);
            }

            await using var content = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            var inspected = await inspector.InspectAsync(content, file.OriginalName, ct);
            var limit =
                await limits.GetAsync(command.TenantId, ct)
                ?? throw new InvalidOperationException("Tenant quota projection is missing.");
            var uploadPolicy = await uploadPolicies.ResolveAsync(
                command.TenantId,
                limit,
                file.FolderType,
                forCustomerPortal: false,
                ct
            );

            var reading = await ReadVerdictsAsync(
                command,
                file,
                inspected,
                uploadPolicy,
                content,
                virusScanner,
                contentScanner,
                ct
            );

            // Segunda transacción corta, con su outbox: veredicto, cuota, auditoría y avisos.
            await bus.InvokeAsync(
                new RecordScanVerdictCommand(
                    command.TenantId,
                    file.Id,
                    command.CorrelationId,
                    command.PortalCustomerId,
                    sourceBucket,
                    reading.Findings,
                    reading.Report,
                    inspected.ContentType,
                    inspected.Sha256
                ),
                ct
            );
        }
        // Sin catch: un fallo transitorio (ClamAV, MinIO, ContentScanner) sube a Wolverine, que reintenta y
        // luego lo manda a la DLQ; el archivo conserva su reserva y StuckScanRescueService lo recoge.
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private sealed record VerdictReading(ScanFindings Findings, string? Report);

    // El orden importa: lo que no pasa el inspector no llega al antivirus, ni lo infectado a moderación.
    private static async Task<VerdictReading> ReadVerdictsAsync(
        ScanFileCommand command,
        FileObject file,
        InspectedContent inspected,
        EffectiveUploadPolicy uploadPolicy,
        Stream content,
        IVirusScanner virusScanner,
        IContentScanner contentScanner,
        CancellationToken ct
    )
    {
        if (
            !inspected.IsSafe
            || !FileTypeCompatibility.Matches(file.OriginalName, inspected.ContentType)
            || !uploadPolicy.AllowedContentTypes.Contains(inspected.ContentType, StringComparer.OrdinalIgnoreCase)
        )
            return new(new ScanFindings(ContentAllowed: false), inspected.RejectionReason ?? ContentNotAllowedReport);

        content.Position = 0;
        var scan = await virusScanner.ScanAsync(content, ct);
        if (scan.Verdict == VirusScanVerdict.Infected)
            return new(new ScanFindings(ContentAllowed: true, Infected: true), scan.Report);
        if (scan.Verdict != VirusScanVerdict.Clean)
            throw new VirusScanUnavailableException($"ClamAV could not produce a verdict: {scan.Report}");

        // ClamAV solo mira virus/malware — IContentScanner es la pasada aparte de
        // moderacion de contenido (T-20); la politica en BD decide si aplica.
        content.Position = 0;
        var contentScan = await contentScanner.ScanAsync(
            content,
            new ContentScanContext(
                command.TenantId,
                file.Id,
                file.OwnerType,
                file.OriginalName,
                inspected.ContentType,
                file.FolderType
            ),
            ct
        );
        return contentScan.Verdict switch
        {
            ContentScanVerdict.Clean => new(new ScanFindings(ContentAllowed: true), null),
            ContentScanVerdict.PolicyViolation => new(
                new ScanFindings(ContentAllowed: true, Moderation: ScanModeration.PolicyViolation),
                contentScan.Reason ?? ModerationWithoutDetailReport
            ),
            _ => new(
                new ScanFindings(ContentAllowed: true, Moderation: ScanModeration.Uncertain),
                contentScan.Reason ?? ModerationWithoutDetailReport
            ),
        };
    }

    /// <summary>Busca el objeto en temp → main → cuarentena (un intento anterior pudo moverlo); null si no está.</summary>
    internal static async Task<string?> LocateStoredObjectAsync(
        IObjectStorage storage,
        CloudStorageOptions options,
        string objectKey,
        CancellationToken ct
    )
    {
        foreach (var bucket in new[] { options.TempBucket, options.MainBucket, options.QuarantineBucket })
        {
            if (await storage.ExistsAsync(bucket, objectKey, ct))
                return bucket;
        }
        return null;
    }
}
