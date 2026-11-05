using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;

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
        IObjectStorage storage,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ILogger<RelocateScannedBlobCommand> logger,
        CancellationToken ct
    )
    {
        // Idempotente: copia (si hay destino) y después borra el origen.
        if (await storage.ExistsAsync(command.SourceBucket, command.ObjectKey, ct))
        {
            if (command.TargetBucket is not null)
                await storage.CopyAsync(command.SourceBucket, command.ObjectKey, command.TargetBucket, ct);
            await storage.DeleteAsync(command.SourceBucket, command.ObjectKey, ct);
            return;
        }

        // Sin origen: un borrado ya está hecho y un movimiento ya llegó si el destino lo tiene.
        if (command.TargetBucket is null || await storage.ExistsAsync(command.TargetBucket, command.ObjectKey, ct))
            return;

        logger.LogError(
            "Scanned blob of file {FileId} is missing from {SourceBucket} and {TargetBucket}.",
            command.FileId,
            command.SourceBucket,
            command.TargetBucket
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
                $"source={command.SourceBucket};target={command.TargetBucket}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}
