using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// X-5 — consumer de <see cref="FilesDeletionRequestedIntegrationEvent"/> (cola
/// <c>cloudstorage-file-deletions</c>). Verifica cada archivo contra su dueño actual, carpeta,
/// legal hold y existencia; si todo cuadra hace borrado blando (papelera) y publica el
/// <see cref="FileDeletedIntegrationEvent"/> de siempre. Si algo no coincide, salta publicando
/// <see cref="FileDeletionSkippedIntegrationEvent"/> para que el origen reintente con el dueño nuevo.
/// </summary>
public static class FilesDeletionRequestedConsumer
{
    public static async Task Handle(
        FilesDeletionRequestedIntegrationEvent message,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FileDeletionSkippedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(message.CorrelationId))
        {
            var retention = TimeSpan.FromDays(options.Value.RecycleBinRetentionDays);
            foreach (var target in message.Files)
            {
                var file = await files.GetAsync(message.TenantId, target.FileId, ct);
                var skip = Evaluate(file, target);
                if (skip is not null)
                {
                    await SkipAsync(message, target.FileId, skip, bus, audit, clock, correlation);
                    continue;
                }

                // Ya en papelera: no se vuelve a anunciar, pero tampoco se trata como fallo.
                if (file!.Status == FileStatus.SoftDeleted)
                {
                    await SkipAsync(message, target.FileId, "already_deleted", bus, audit, clock, correlation);
                    continue;
                }

                var softDeleted = file.SoftDelete(clock.UtcNow, retention);
                if (softDeleted.IsFailure)
                {
                    await SkipAsync(message, target.FileId, "legal_hold", bus, audit, clock, correlation);
                    continue;
                }

                audit.Add(
                    StorageAccessLog.Create(
                        message.TenantId,
                        file.Id,
                        Guid.Empty,
                        "delete.retention",
                        "success",
                        null,
                        null,
                        correlation.CorrelationId,
                        $"reason={message.Reason};source={message.SourceService}",
                        clock.UtcNow
                    )
                );
                await bus.PublishAsync(
                    new FileDeletedIntegrationEvent
                    {
                        TenantId = message.TenantId,
                        FileId = file.Id,
                        CreatedBy = file.CreatedBy,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // El dueño esperado es el ACTUAL del archivo; una reasignación entre cálculo y ejecución da owner_mismatch.
    private static string? Evaluate(FileObject? file, FileDeletionTarget target)
    {
        if (file is null)
            return "not_found";
        if (file.IsLegalHeld)
            return "legal_hold";
        if (!file.FolderType.ToString().Equals(target.ExpectedFolderType, StringComparison.OrdinalIgnoreCase))
            return "folder_mismatch";
        if (
            !file.OwnerType.ToString().Equals(target.ExpectedOwnerType, StringComparison.OrdinalIgnoreCase)
            || file.OwnerId != target.ExpectedOwnerId
        )
            return "owner_mismatch";
        return null;
    }

    private static async Task SkipAsync(
        FilesDeletionRequestedIntegrationEvent message,
        Guid fileId,
        string reason,
        IMessageBus bus,
        IStorageAuditRepository audit,
        ISystemClock clock,
        ICorrelationContext correlation
    )
    {
        audit.Add(
            StorageAccessLog.Create(
                message.TenantId,
                fileId,
                Guid.Empty,
                "delete.skipped",
                reason,
                null,
                null,
                correlation.CorrelationId,
                $"source={message.SourceService}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileDeletionSkippedIntegrationEvent
            {
                TenantId = message.TenantId,
                FileId = fileId,
                Reason = reason,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
