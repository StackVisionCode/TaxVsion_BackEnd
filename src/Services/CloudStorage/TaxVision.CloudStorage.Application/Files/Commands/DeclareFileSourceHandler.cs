using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// Consumidor de <see cref="FileSourceDeclaredIntegrationEvent"/>: el servicio dueño dice de qué recurso nace un
/// archivo ya catalogado. Idempotente. Una declaración que no encaja en <see cref="FileSourceCatalog"/> se
/// descarta y queda auditada, sin reintento.
/// </summary>
public static class DeclareFileSourceHandler
{
    public static async Task Handle(
        FileSourceDeclaredIntegrationEvent evt,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<FileSourceDeclaredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var file = await files.GetAsync(evt.TenantId, evt.FileId, ct);
            if (file is null)
            {
                // El dueño conoce el FileId antes de que SaveFileRequested termine: si es reciente, se espera.
                if (clock.UtcNow - evt.OccurredOn < TimeSpan.FromMinutes(options.Value.ReassignOwnerFileWaitMinutes))
                    throw new DependencyNotReadyException("FileObject", $"file {evt.FileId} is not cataloged yet");

                logger.LogWarning("DeclareFileSource {FileId}: file not found, skipping.", evt.FileId);
                await AuditAsync(
                    evt,
                    "source.declare-skipped",
                    "reason=file_not_found",
                    audit,
                    unitOfWork,
                    correlation,
                    clock,
                    ct
                );
                return;
            }

            var declaration = FileSourceDeclaration.Create(
                evt.SourceService,
                evt.SourceResourceType,
                evt.SourceResourceId,
                evt.SourceRole,
                evt.RelatedCustomerId
            );
            if (
                declaration.IsFailure
                || !FileSourceCatalog.Accepts(evt.SourceResourceType, evt.SourceRole, file.OwnerType, file.FolderType)
            )
            {
                logger.LogWarning(
                    "DeclareFileSource {FileId}: {ResourceType}/{Role} does not fit {OwnerType}/{FolderType}; rejected.",
                    evt.FileId,
                    evt.SourceResourceType,
                    evt.SourceRole,
                    file.OwnerType,
                    file.FolderType
                );
                var details =
                    $"source={evt.SourceResourceType}/{evt.SourceRole};owner={file.OwnerType}/{file.FolderType}";
                await AuditAsync(evt, "source.declare-rejected", details, audit, unitOfWork, correlation, clock, ct);
                return;
            }

            // Sin cambio no se guarda: la reconciliación repite declaraciones.
            if (file.DeclareSource(declaration.Value, evt.OccurredOn) is { IsSuccess: true, Value: true })
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static Task AuditAsync(
        FileSourceDeclaredIntegrationEvent evt,
        string action,
        string details,
        IStorageAuditRepository audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        // Sin persona: lo declara un servicio.
        audit.Add(
            StorageAccessLog.Create(
                evt.TenantId,
                evt.FileId,
                Guid.Empty,
                action,
                "skipped",
                null,
                null,
                correlation.CorrelationId,
                $"service={evt.SourceService};{details}",
                clock.UtcNow
            )
        );
        return unitOfWork.SaveChangesAsync(ct);
    }
}
