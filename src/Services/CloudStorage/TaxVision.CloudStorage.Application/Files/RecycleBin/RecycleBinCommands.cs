using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>Fase C1 — recupera un archivo de la papelera. No toca cuota (ver TenantStorageLimit.ReleaseUsed).</summary>
public sealed record RestoreFileCommand(Guid TenantId, Guid ActorId, Guid FileId, RequestAuditContext Audit);

public static class RestoreFileHandler
{
    public static async Task<Result<FileResponse>> Handle(
        RestoreFileCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var loaded = await LoadFile(command, files, ct);
        if (loaded.IsFailure)
            return Result.Failure<FileResponse>(loaded.Error);

        var file = loaded.Value;
        var restored = file.Restore();
        if (restored.IsFailure)
            return Result.Failure<FileResponse>(restored.Error);

        await PersistAndPublish(command, file, audit, clock, unitOfWork, bus, ct);
        return Result.Success(FileResponseMapper.Map(file));
    }

    private static async Task<Result<FileObject>> LoadFile(
        RestoreFileCommand command,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        return file is null ? Result.Failure<FileObject>(FileErrors.NotFound) : Result.Success(file);
    }

    private static async Task PersistAndPublish(
        RestoreFileCommand command,
        FileObject file,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "restore",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileRestoredIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                CreatedBy = file.CreatedBy,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Fase C1 — purga inmediata y manual de TODO lo que hay en la papelera del tenant,
/// sin esperar a que venza la retencion (esa purga automatica es
/// RecycleBinPurgeService, el job diario). Ambas rutas comparten RecycleBinPurger
/// para el borrado fisico — evita reimplementar la misma logica dos veces.
/// </summary>
public sealed record EmptyRecycleBinCommand(Guid TenantId, Guid ActorId, RequestAuditContext Audit);

public static class EmptyRecycleBinHandler
{
    private const int MaxBatchSize = 500;

    public static async Task<int> Handle(
        EmptyRecycleBinCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ILogger<EmptyRecycleBinCommand> logger,
        CancellationToken ct
    )
    {
        var candidates = await files.ListSoftDeletedAsync(command.TenantId, 0, MaxBatchSize, ct);
        var purgedCount = await PurgeEach(
            candidates,
            command,
            options.Value.MainBucket,
            files,
            limits,
            audit,
            storage,
            clock,
            logger,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return purgedCount;
    }

    private static async Task<int> PurgeEach(
        IReadOnlyList<FileObject> candidates,
        EmptyRecycleBinCommand command,
        string mainBucket,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        ISystemClock clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        var purgedCount = 0;
        foreach (var file in candidates)
        {
            var purged = await RecycleBinPurger.PurgeAsync(
                file,
                "manual",
                mainBucket,
                files,
                limits,
                audit,
                storage,
                clock,
                command.ActorId,
                command.Audit.CorrelationId,
                logger,
                ct
            );
            if (purged)
                purgedCount++;
        }
        return purgedCount;
    }
}
