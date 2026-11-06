using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Storage;

/// <summary>
/// CloudStorage rechazó un archivo que pidió Scribe: ese FileId nunca va a existir, así que se suelta
/// donde todavía se apunte. El logo de plataforma se borra (LogoResolver lo trata como "sin sembrar" y
/// ScribeSystemAssetSeeder lo vuelve a sembrar en el próximo arranque); las Draft de templates/layouts
/// sueltan las referencias opcionales. El HTML de una Draft es obligatorio: solo se avisa en el log.
/// Seeders y break-glass del logo nunca guardan un FileId sin confirmar que se descarga, así que
/// lo normal es que acá lleguen Drafts guardadas desde la API.
/// </summary>
public static class FileSaveRejectedConsumer
{
    // Clave del L1 que arma LogoResolver para el scope System.
    private const string SystemLogoCacheKey = "logo:system";

    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        IEmailTemplateRepository templateRepository,
        IEmailLayoutRepository layoutRepository,
        ISystemAssetRefRepository systemAssetRefRepository,
        IUnitOfWork unitOfWork,
        IMemoryCache l1Cache,
        IOptions<FileSaveRejectedOptions> options,
        ICorrelationContext correlation,
        ILogger<FileSaveRejectedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        // El exchange es fanout: llegan los rechazos de todos los servicios.
        if (!string.Equals(evt.RequestingService, ScribeStorageIdentity.RequestingService, StringComparison.Ordinal))
            return;

        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var now = DateTime.UtcNow;
            var referenced = false;
            var released = false;

            // Solo si sigue apuntando a este FileId: un logo más nuevo no se toca.
            var headerLogo = await systemAssetRefRepository.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
            var headerLogoReleased = headerLogo is not null && headerLogo.CloudStorageFileId == evt.FileId;
            if (headerLogoReleased)
            {
                systemAssetRefRepository.Remove(headerLogo!);
                referenced = released = true;
            }

            foreach (var template in await templateRepository.GetReferencingFileAsync(evt.FileId, ct))
            {
                referenced = true;
                foreach (var versionNumber in template.DraftsBlockedByRejectedFile(evt.FileId))
                    logger.LogWarning(
                        "Template {TemplateId} draft v{VersionNumber} content file {FileId} was rejected by CloudStorage ({Reason}); it cannot be published until a new draft is saved.",
                        template.Id,
                        versionNumber,
                        evt.FileId,
                        evt.Reason
                    );
                released |= template.ReleaseRejectedFile(evt.FileId, now);
            }

            foreach (var layout in await layoutRepository.GetReferencingFileAsync(evt.FileId, ct))
            {
                referenced = true;
                foreach (var versionNumber in layout.DraftsBlockedByRejectedFile(evt.FileId))
                    logger.LogWarning(
                        "Layout {LayoutId} draft v{VersionNumber} HTML file {FileId} was rejected by CloudStorage ({Reason}); it cannot be published until a new draft is saved.",
                        layout.Id,
                        versionNumber,
                        evt.FileId,
                        evt.Reason
                    );
                released |= layout.ReleaseRejectedFile(evt.FileId, now);
            }

            if (!referenced)
            {
                // Puede haber llegado antes de que se guarde la versión: se reintenta con espera (BB-7).
                if (now - evt.OccurredOn < options.Value.EarlyRejectionWindow)
                    throw new DependencyNotReadyException("ScribeFileReference", $"file {evt.FileId}");

                logger.LogWarning(
                    "FileSaveRejected {FileId} ({Reason}): nothing in Scribe references it, nothing to release.",
                    evt.FileId,
                    evt.Reason
                );
                return;
            }

            if (!released)
                return;

            await unitOfWork.SaveChangesAsync(ct);
            if (headerLogoReleased)
                l1Cache.Remove(SystemLogoCacheKey);

            logger.LogWarning(
                "FileSaveRejected {FileId} ({Reason}): released its Scribe references (header logo: {HeaderLogo}).",
                evt.FileId,
                evt.Reason,
                headerLogoReleased
            );
        }
    }
}
