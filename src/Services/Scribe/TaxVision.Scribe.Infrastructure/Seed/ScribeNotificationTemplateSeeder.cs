using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>
/// Fase 8 — siembra los 13 templates migrados desde Notification
/// (<see cref="NotificationTemplateSeedSource"/>) al arrancar, si todavía no existen: sube el HTML a
/// CloudStorage, crea EmailTemplate + versión Published sobre el layout <c>system-base</c>
/// (sembrado antes por <see cref="ScribeBaseLayoutSeeder"/>, de ahí que este seeder corra después en
/// Program.cs), y el EventTemplateMapping correspondiente. Mismo patrón que
/// <see cref="ScribeBaseLayoutSeeder"/>: cada fallo se loguea y se sigue con el próximo, nunca
/// bloquea el arranque del servicio.
/// </summary>
public sealed class ScribeNotificationTemplateSeeder(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<ScribeNotificationTemplateSeeder> logger
) : IHostedService
{
    private const int SeedDependencyWaitAttempts = 180;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Mismo motivo que ScribeBaseLayoutSeeder: SeedIfMissingAsync sube a CloudStorage y eso
        // publica vía Wolverine, que recién arranca cuando TODO el host terminó StartAsync.
        lifetime.ApplicationStarted.Register(() => _ = SeedAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();
            var storageService = scope.ServiceProvider.GetRequiredService<ITemplateStorageService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var systemBaseKey = LayoutKey.Create("system-base").Value;
            EmailLayout? systemBaseLayout = null;
            EmailLayoutVersion? publishedLayoutVersion = null;
            for (var attempt = 1; attempt <= SeedDependencyWaitAttempts; attempt++)
            {
                systemBaseLayout = await dbContext
                    .EmailLayouts.Include(l => l.Versions)
                    .FirstOrDefaultAsync(
                        l => l.Scope == TemplateScope.System && l.LayoutKey == systemBaseKey,
                        cancellationToken
                    );
                publishedLayoutVersion = systemBaseLayout?.Versions.FirstOrDefault(v =>
                    v.Status == EmailVersionStatus.Published
                );
                if (publishedLayoutVersion is not null)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (systemBaseLayout is null || publishedLayoutVersion is null)
            {
                logger.LogWarning(
                    "ScribeNotificationTemplateSeeder skipped after waiting {Seconds}s: 'system-base' layout is not published.",
                    SeedDependencyWaitAttempts
                );
                return;
            }

            var seeded = 0;
            foreach (var definition in NotificationTemplateSeedSource.All)
            {
                var ok = await SeedIfMissingAsync(
                    dbContext,
                    storageService,
                    unitOfWork,
                    definition,
                    systemBaseLayout.Id,
                    publishedLayoutVersion.VersionNumber,
                    cancellationToken
                );
                if (ok)
                    seeded++;
            }

            logger.LogInformation(
                "ScribeNotificationTemplateSeeder: {Seeded}/{Total} templates seeded (existing ones skipped).",
                seeded,
                NotificationTemplateSeedSource.All.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed notification templates on startup.");
        }
    }

    private async Task<bool> SeedIfMissingAsync(
        ScribeDbContext dbContext,
        ITemplateStorageService storageService,
        IUnitOfWork unitOfWork,
        NotificationTemplateSeed definition,
        Guid layoutId,
        int layoutVersionNumber,
        CancellationToken ct
    )
    {
        var templateKeyResult = TemplateKey.Create(definition.TemplateKey);
        var eventKeyResult = EventKey.Create(definition.EventKey);
        if (templateKeyResult.IsFailure || eventKeyResult.IsFailure)
        {
            logger.LogError(
                "Invalid template/event key for seed '{Name}': {TemplateError} {EventError}",
                definition.Name,
                templateKeyResult.IsFailure ? templateKeyResult.Error.Message : null,
                eventKeyResult.IsFailure ? eventKeyResult.Error.Message : null
            );
            return false;
        }
        var templateKey = templateKeyResult.Value;
        var eventKey = eventKeyResult.Value;

        var existingTemplate = await dbContext
            .EmailTemplates.Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Scope == TemplateScope.System && t.TemplateKey == templateKey, ct);
        if (existingTemplate is not null)
        {
            // Bug real: si el EmailTemplate ya existe pero el EventTemplateMapping nunca se
            // creó (p.ej. porque CreateNew falló en un arranque previo mientras el
            // AddAsync del template ya había quedado en el change tracker y se guardó igual
            // en el SaveChanges de la siguiente iteración del loop), este seeder nunca lo
            // detectaba: la rama "ya existe" solo reparaba el archivo, nunca el mapping. El
            // render por eventKey quedaba en 404 para siempre, sin importar cuántos reinicios.
            var mappingRepaired = await EnsureMappingAsync(
                dbContext,
                unitOfWork,
                eventKey,
                templateKey,
                definition,
                logger,
                ct
            );

            var publishedVersion = existingTemplate
                .Versions.Where(v => v.Status == EmailVersionStatus.Published)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            if (publishedVersion?.HtmlFileId is Guid htmlFileId)
            {
                var download = await storageService.DownloadTextAsync(htmlFileId, tenantId: null, ct);
                if (download.IsSuccess)
                    return mappingRepaired;

                logger.LogWarning(
                    "Published template '{TemplateKey}' references missing CloudStorage file {FileId}; repairing it.",
                    definition.TemplateKey,
                    htmlFileId
                );
            }

            var repairUpload = await storageService.UploadAsync(
                tenantId: null,
                TemplateArtifactKind.Html,
                Encoding.UTF8.GetBytes(definition.Html),
                PlatformTenant.Id,
                ct
            );
            if (repairUpload.IsFailure)
            {
                logger.LogError(
                    "Failed to repair template '{TemplateKey}': {Error}",
                    definition.TemplateKey,
                    repairUpload.Error.Message
                );
                return false;
            }

            if (!await WaitUntilDownloadableAsync(storageService, repairUpload.Value.FileId, ct))
            {
                logger.LogError(
                    "CloudStorage did not catalogue repaired template '{TemplateKey}' file {FileId} in time.",
                    definition.TemplateKey,
                    repairUpload.Value.FileId
                );
                return false;
            }

            var repairVersion = existingTemplate.AddDraftVersion(
                definition.Subject,
                repairUpload.Value.StorageKey,
                repairUpload.Value.FileId,
                null,
                null,
                null,
                null,
                null,
                null,
                layoutId,
                layoutVersionNumber,
                definition.Variables,
                DateTime.UtcNow
            );
            if (repairVersion.IsFailure)
            {
                logger.LogError(
                    "Failed to create repaired version for template '{TemplateKey}': {Error}",
                    definition.TemplateKey,
                    repairVersion.Error.Message
                );
                return false;
            }

            existingTemplate.PublishVersion(repairVersion.Value.Id, PlatformTenant.Id, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Repaired and published template '{TemplateKey}'.", definition.TemplateKey);
            return true;
        }

        var uploadResult = await storageService.UploadAsync(
            tenantId: null,
            TemplateArtifactKind.Html,
            Encoding.UTF8.GetBytes(definition.Html),
            PlatformTenant.Id,
            ct
        );
        if (uploadResult.IsFailure)
        {
            logger.LogError(
                "Failed to upload template '{TemplateKey}': {Error}",
                definition.TemplateKey,
                uploadResult.Error.Message
            );
            return false;
        }

        if (!await WaitUntilDownloadableAsync(storageService, uploadResult.Value.FileId, ct))
        {
            logger.LogError(
                "CloudStorage did not catalogue template '{TemplateKey}' file {FileId} in time.",
                definition.TemplateKey,
                uploadResult.Value.FileId
            );
            return false;
        }

        var templateResult = EmailTemplate.CreateNew(
            TemplateScope.System,
            null,
            templateKey,
            definition.Name,
            null,
            PlatformTenant.Id,
            DateTime.UtcNow
        );
        if (templateResult.IsFailure)
        {
            logger.LogError(
                "Failed to create template '{TemplateKey}': {Error}",
                definition.TemplateKey,
                templateResult.Error.Message
            );
            return false;
        }

        var template = templateResult.Value;
        var versionResult = template.AddDraftVersion(
            definition.Subject,
            uploadResult.Value.StorageKey,
            uploadResult.Value.FileId,
            null,
            null,
            null,
            null,
            null,
            null,
            layoutId,
            layoutVersionNumber,
            definition.Variables,
            DateTime.UtcNow
        );
        if (versionResult.IsFailure)
        {
            logger.LogError(
                "Failed to add draft version to template '{TemplateKey}': {Error}",
                definition.TemplateKey,
                versionResult.Error.Message
            );
            return false;
        }

        template.PublishVersion(versionResult.Value.Id, PlatformTenant.Id, DateTime.UtcNow);
        await dbContext.EmailTemplates.AddAsync(template, ct);

        var mappingResult = EventTemplateMapping.CreateNew(
            TemplateScope.System,
            null,
            eventKey,
            templateKey,
            null,
            0,
            DateTime.UtcNow
        );
        if (mappingResult.IsFailure)
        {
            logger.LogError(
                "Failed to create event mapping for '{EventKey}': {Error}",
                definition.EventKey,
                mappingResult.Error.Message
            );
            return false;
        }
        await dbContext.EventTemplateMappings.AddAsync(mappingResult.Value, ct);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Seeded and published template '{TemplateKey}' mapped from event '{EventKey}'.",
            definition.TemplateKey,
            definition.EventKey
        );
        return true;
    }

    /// <summary>
    /// Crea el EventTemplateMapping System-scope si falta, para un template que ya existe.
    /// Ver comentario en el llamador: sin esto, un template huérfano de su mapping (por una
    /// falla parcial de un seed anterior) nunca se repara, sin importar cuántos reinicios.
    /// </summary>
    private static async Task<bool> EnsureMappingAsync(
        ScribeDbContext dbContext,
        IUnitOfWork unitOfWork,
        EventKey eventKey,
        TemplateKey templateKey,
        NotificationTemplateSeed definition,
        ILogger<ScribeNotificationTemplateSeeder> logger,
        CancellationToken ct
    )
    {
        var existingMapping = await dbContext.EventTemplateMappings.FirstOrDefaultAsync(
            m => m.Scope == TemplateScope.System && m.TenantId == null && m.EventKey == eventKey,
            ct
        );
        if (existingMapping is not null)
            return false;

        var mappingResult = EventTemplateMapping.CreateNew(
            TemplateScope.System,
            null,
            eventKey,
            templateKey,
            null,
            0,
            DateTime.UtcNow
        );
        if (mappingResult.IsFailure)
        {
            logger.LogError(
                "Failed to repair missing event mapping for '{EventKey}': {Error}",
                definition.EventKey,
                mappingResult.Error.Message
            );
            return false;
        }

        await dbContext.EventTemplateMappings.AddAsync(mappingResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Repaired missing event mapping for '{EventKey}' -> '{TemplateKey}'.",
            definition.EventKey,
            definition.TemplateKey
        );
        return true;
    }

    private static async Task<bool> WaitUntilDownloadableAsync(
        ITemplateStorageService storageService,
        Guid fileId,
        CancellationToken ct
    )
    {
        for (var attempt = 1; attempt <= SeedDependencyWaitAttempts; attempt++)
        {
            var download = await storageService.DownloadTextAsync(fileId, null, ct);
            if (download.IsSuccess)
                return true;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return false;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
