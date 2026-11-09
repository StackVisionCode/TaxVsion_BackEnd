using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// CST-3: mueve (o borra, con <paramref name="TargetBucket"/> null) el objeto escaneado. Lo publica
/// ScanFileHandler después de guardar, así que solo sale si la transacción del veredicto confirmó.
/// </summary>
public sealed record RelocateScannedBlobCommand(
    Guid TenantId,
    Guid FileId,
    string ObjectKey,
    string SourceBucket,
    string? TargetBucket,
    Guid ActorId,
    string CorrelationId
);

public static class RelocateScannedBlobHandler
{
    public static async Task Handle(
        RelocateScannedBlobCommand command,
        IFileObjectRepository files,
        IOptions<CloudStorageOptions> options,
        IObjectStorage storage,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ILogger<RelocateScannedBlobCommand> logger,
        CancellationToken ct
    )
    {
        var targetBucket = await CurrentTargetAsync(command, files, options.Value, ct);

        // Idempotente: copia (si hay destino) y después borra el origen.
        if (await storage.ExistsAsync(command.SourceBucket, command.ObjectKey, ct))
        {
            if (targetBucket is not null)
                await storage.CopyAsync(command.SourceBucket, command.ObjectKey, targetBucket, ct);
            await storage.DeleteAsync(command.SourceBucket, command.ObjectKey, ct);
            return;
        }

        // Sin origen: un borrado ya está hecho y un movimiento ya llegó si el destino lo tiene.
        if (targetBucket is null || await storage.ExistsAsync(targetBucket, command.ObjectKey, ct))
            return;

        logger.LogError(
            "Scanned blob of file {FileId} is missing from {SourceBucket} and {TargetBucket}.",
            command.FileId,
            command.SourceBucket,
            targetBucket
        );
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                command.FileId,
                command.ActorId,
                "blob.relocate",
                "missing",
                null,
                null,
                command.CorrelationId,
                $"source={command.SourceBucket};target={targetBucket}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
    }

    // Si un revisor aprobó antes de que llegara el comando, el destino es main, no cuarentena.
    private static async Task<string?> CurrentTargetAsync(
        RelocateScannedBlobCommand command,
        IFileObjectRepository files,
        CloudStorageOptions options,
        CancellationToken ct
    )
    {
        if (command.TargetBucket != options.QuarantineBucket)
            return command.TargetBucket;
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        return file?.Status == FileStatus.Available ? options.MainBucket : command.TargetBucket;
    }
}
