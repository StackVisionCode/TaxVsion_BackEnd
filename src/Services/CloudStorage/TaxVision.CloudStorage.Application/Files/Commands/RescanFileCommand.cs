using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>CST-2: PlatformAdmin (o la reposición) vuelve a escanear un ScanFailed cuyo objeto sigue guardado.</summary>
public sealed record RescanFileCommand(Guid TenantId, Guid FileId, Guid ActorId);

public static class RescanFileHandler
{
    public static async Task<Result> Handle(
        RescanFileCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);
        if (file.Status != FileStatus.ScanFailed)
            return Result.Failure(FileErrors.InvalidTransition);

        var config = options.Value;
        var blobFound =
            await storage.ExistsAsync(config.TempBucket, file.ObjectKey, ct)
            || await storage.ExistsAsync(config.MainBucket, file.ObjectKey, ct);
        if (!blobFound)
            return Result.Failure(FileErrors.BlobMissing);

        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure(QuotaErrors.NotProvisioned);
        var reserved = limit.Reserve(file.SizeBytes);
        if (reserved.IsFailure)
            return reserved;

        var rescan = file.RequestRescan(clock.UtcNow);
        if (rescan.IsFailure)
        {
            limit.Release(file.SizeBytes);
            return rescan;
        }

        var correlationId = $"rescan-{file.Id:N}";
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "scan.rescan",
                "pending-scan",
                null,
                null,
                correlationId,
                null,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new ScanFileCommand(command.TenantId, file.Id, correlationId));
        return Result.Success();
    }
}
