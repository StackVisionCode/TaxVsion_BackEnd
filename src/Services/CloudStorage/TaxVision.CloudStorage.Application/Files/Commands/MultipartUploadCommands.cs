using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>Fase U — arranca un upload multiparte: mismas reglas de negocio que InitiateUploadHandler (ver UploadRegistration), pero devuelve N URLs presignadas de parte en vez de una sola.</summary>
public sealed record InitiateMultipartUploadCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    InitiateMultipartUploadRequest Request,
    RequestAuditContext Audit
);

public static class InitiateMultipartUploadHandler
{
    public static async Task<Result<InitiatedMultipartUploadResponse>> Handle(
        InitiateMultipartUploadCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectKeyBuilder keyBuilder,
        IMultipartUploadStorage multipartStorage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var request = command.Request;
        var config = options.Value;

        var registered = await UploadRegistration.ReserveAndRegisterAsync(
            command.TenantId,
            command.ActorId,
            command.Scope,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            request.TaxYear,
            request.OriginalName,
            request.ContentType,
            request.SizeBytes,
            command.Audit.CorrelationId,
            limits,
            keyBuilder,
            config,
            clock,
            bus,
            ct
        );
        if (registered.IsFailure)
            return Result.Failure<InitiatedMultipartUploadResponse>(registered.Error);

        var file = registered.Value;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(config.PresignedUrlMinutes, 1, 60));
        var initiation = await multipartStorage.InitiateAsync(
            config.TempBucket,
            file.ObjectKey,
            request.ContentType,
            request.SizeBytes,
            config.MultipartPartSizeBytes,
            lifetime,
            ct
        );
        // Nunca falla en la practica (Status siempre es PendingUpload recien Register()),
        // pero Result.Success() no expone el valor si esto llegara a fallar silenciosamente.
        file.AttachMultipartUpload(initiation.UploadId);

        files.Add(file);
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "upload.multipart-initiated",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"size={request.SizeBytes};parts={initiation.Parts.Count}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                ActorId = command.ActorId,
                Action = "upload.multipart-initiated",
                Outcome = "success",
                IpAddress = command.Audit.IpAddress,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new InitiatedMultipartUploadResponse(
                file.Id,
                initiation.UploadId,
                initiation.Parts.Select(p => new MultipartPartUploadUrlResponse(p.PartNumber, p.UploadUrl)).ToArray(),
                clock.UtcNow.Add(lifetime)
            )
        );
    }
}

/// <summary>Fase U — ensambla las partes ya subidas por el cliente y despues sigue exactamente el mismo camino que CompleteUploadHandler (verificar tamano, MarkPendingScan, disparar el escaneo) — el pipeline de ahi en mas no distingue si el objeto llego por POST unico o por multipart.</summary>
public sealed record CompleteMultipartUploadCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    CompleteMultipartUploadRequest Request,
    RequestAuditContext Audit
);

public static class CompleteMultipartUploadHandler
{
    public static async Task<Result> Handle(
        CompleteMultipartUploadCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IMultipartUploadStorage multipartStorage,
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
        if (!command.Scope.CanAccess(file))
            return Result.Failure(FileErrors.NotFound);
        if (file.Status != FileStatus.PendingUpload)
            return Result.Failure(FileErrors.InvalidTransition);

        var parts = command.Request.Parts.Select(p => new MultipartPart(p.PartNumber, p.ETag)).ToArray();
        try
        {
            await multipartStorage.CompleteAsync(
                options.Value.TempBucket,
                file.ObjectKey,
                command.Request.UploadId,
                parts,
                ct
            );
        }
        catch (Exception)
        {
            // El ensamblado fallo (ETags invalidos, parte faltante, etc) — abortamos para
            // liberar las partes ya subidas en vez de dejarlas huerfanas en MinIO. El
            // FileObject queda en PendingUpload; ExpiredUploadCleanupService lo termina
            // de limpiar (ExpireUpload) cuando venza la reserva.
            await multipartStorage.AbortAsync(options.Value.TempBucket, file.ObjectKey, command.Request.UploadId, ct);
            return Result.Failure(FileErrors.MultipartCompleteFailed);
        }

        // De aca en mas es identico al flujo de un solo POST: el objeto ya esta
        // ensamblado en TempBucket con la key esperada, asi que CompleteUploadHandler
        // no necesita saber que llego por multipart.
        return await CompleteUploadHandler.Handle(
            new CompleteUploadCommand(command.TenantId, command.ActorId, command.Scope, command.FileId, command.Audit),
            files,
            limits,
            audit,
            storage,
            options,
            clock,
            unitOfWork,
            bus,
            ct
        );
    }
}
