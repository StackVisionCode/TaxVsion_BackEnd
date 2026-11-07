using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Templates.BaseLayouts;
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
    ISeedLock seedLock,
    ILogger<ScribeNotificationTemplateSeeder> logger
) : IHostedService
{
    private const int SeedDependencyWaitAttempts = 180;
    private const string SeedLockResource = "scribe:template-seed";

    private enum SeedOutcome
    {
        Unchanged,
        Seeded,
        Failed,
    }

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
            // SCR-02: dos réplicas sembrando a la vez chocan y duplican subidas.
            await using var held = await seedLock.TryAcquireAsync(SeedLockResource, cancellationToken);
            if (held is null)
            {
                logger.LogInformation("ScribeNotificationTemplateSeeder skipped: another replica is seeding.");
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();

            // Dos layouts, no uno: las plantillas de la plataforma van sobre system-base y las que
            // manda la OFICINA (la factura) sobre tenant-base. Cada seed dice cuál quiere.
            var systemBaseKey = LayoutKey.Create("system-base").Value;
            var tenantBaseKey = LayoutKey.Create("tenant-base").Value;
            EmailLayout? systemBaseLayout = null;
            EmailLayoutVersion? publishedLayoutVersion = null;
            EmailLayout? tenantBaseLayout = null;
            EmailLayoutVersion? publishedTenantLayoutVersion = null;
            for (var attempt = 1; attempt <= SeedDependencyWaitAttempts; attempt++)
            {
                systemBaseLayout = await dbContext
                    .EmailLayouts.Include(l => l.Versions)
                    .FirstOrDefaultAsync(
                        l => l.Scope == TemplateScope.System && l.LayoutKey == systemBaseKey,
                        cancellationToken
                    );
                publishedLayoutVersion = systemBaseLayout
                    ?.Versions.Where(v => v.Status == EmailVersionStatus.Published)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();

                tenantBaseLayout = await dbContext
                    .EmailLayouts.Include(l => l.Versions)
                    .FirstOrDefaultAsync(
                        l => l.Scope == TemplateScope.System && l.LayoutKey == tenantBaseKey,
                        cancellationToken
                    );
                publishedTenantLayoutVersion = tenantBaseLayout
                    ?.Versions.Where(v => v.Status == EmailVersionStatus.Published)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();
                // Esperar a que el layout seeder haya republicado system-base a la versión de contenido
                // actual: los dos seeders corren concurrentes en ApplicationStarted, y capturar una
                // versión vieja deja a los templates pineados a un layout archivado.
                if (
                    publishedLayoutVersion is not null
                    && (publishedLayoutVersion.SeedContentVersion ?? 0) >= BaseLayoutHtml.SystemBaseVersion
                    && publishedTenantLayoutVersion is not null
                    && (publishedTenantLayoutVersion.SeedContentVersion ?? 0) >= BaseLayoutHtml.TenantBaseVersion
                )
                    break;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (
                systemBaseLayout is null
                || publishedLayoutVersion is null
                || tenantBaseLayout is null
                || publishedTenantLayoutVersion is null
            )
            {
                logger.LogWarning(
                    "ScribeNotificationTemplateSeeder skipped after waiting {Seconds}s: system-base or tenant-base is not published.",
                    SeedDependencyWaitAttempts
                );
                return;
            }

            var seeded = 0;
            var failed = 0;
            foreach (var definition in NotificationTemplateSeedSource.All)
            {
                var onTenantBase = definition.LayoutKey == "tenant-base";
                var outcome = await SeedIsolatedAsync(
                    definition,
                    onTenantBase ? tenantBaseLayout.Id : systemBaseLayout.Id,
                    onTenantBase ? publishedTenantLayoutVersion.VersionNumber : publishedLayoutVersion.VersionNumber,
                    cancellationToken
                );
                seeded += outcome == SeedOutcome.Seeded ? 1 : 0;
                failed += outcome == SeedOutcome.Failed ? 1 : 0;
            }

            logger.LogInformation(
                "ScribeNotificationTemplateSeeder: {Seeded} seeded, {Failed} failed of {Total} templates (existing ones skipped).",
                seeded,
                failed,
                NotificationTemplateSeedSource.All.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed notification templates on startup.");
        }
    }

    // SCR-02: scope propio por plantilla. Con un contexto compartido, lo que quedó a medias de una se guardaba con
    // la siguiente, y una excepción cortaba todas las que faltaban.
    private async Task<SeedOutcome> SeedIsolatedAsync(
        NotificationTemplateSeed definition,
        Guid layoutId,
        int layoutVersionNumber,
        CancellationToken ct
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await SeedIfMissingAsync(
                scope.ServiceProvider.GetRequiredService<ScribeDbContext>(),
                scope.ServiceProvider.GetRequiredService<ITemplateStorageService>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                definition,
                layoutId,
                layoutVersionNumber,
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to seed template '{TemplateKey}'.", definition.TemplateKey);
            return SeedOutcome.Failed;
        }
    }

    private async Task<SeedOutcome> SeedIfMissingAsync(
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
            return SeedOutcome.Failed;
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
            var publishedFileId =
                definition.Channel == TemplateChannel.Push
                    ? publishedVersion?.TextFileId
                    : publishedVersion?.HtmlFileId;
            if (publishedFileId is Guid contentFileId)
            {
                var stored = publishedVersion!.SeedContentVersion ?? 0;
                // Layout pineado viejo: el render usa version.LayoutVersionNumber, así que si el layout
                // republicó a una versión nueva hay que re-pinear el template aunque el contenido no cambie.
                var layoutStale =
                    definition.Channel == TemplateChannel.Email
                    && publishedVersion.LayoutVersionNumber != layoutVersionNumber;
                if (stored >= definition.ContentVersion && !layoutStale)
                {
                    var download = await storageService.DownloadTextAsync(contentFileId, tenantId: null, ct);
                    if (download.IsSuccess)
                        return mappingRepaired ? SeedOutcome.Seeded : SeedOutcome.Unchanged;

                    logger.LogWarning(
                        "Published template '{TemplateKey}' references missing CloudStorage file {FileId}; repairing it.",
                        definition.TemplateKey,
                        contentFileId
                    );
                }
                else if (layoutStale)
                {
                    logger.LogInformation(
                        "Template '{TemplateKey}' layout v{OldLayout} -> v{NewLayout}; republishing.",
                        definition.TemplateKey,
                        publishedVersion.LayoutVersionNumber,
                        layoutVersionNumber
                    );
                }
                else
                {
                    logger.LogInformation(
                        "Template '{TemplateKey}' content v{Stored} -> v{New}; republishing.",
                        definition.TemplateKey,
                        stored,
                        definition.ContentVersion
                    );
                }
            }

            var repairUpload = await storageService.UploadAsync(
                tenantId: null,
                definition.Channel == TemplateChannel.Push ? TemplateArtifactKind.Text : TemplateArtifactKind.Html,
                Encoding.UTF8.GetBytes(definition.Channel == TemplateChannel.Push ? definition.Text! : definition.Html),
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
                return SeedOutcome.Failed;
            }

            if (!await WaitUntilDownloadableAsync(storageService, repairUpload.Value.FileId, ct))
            {
                logger.LogError(
                    "CloudStorage did not catalogue repaired template '{TemplateKey}' file {FileId} in time.",
                    definition.TemplateKey,
                    repairUpload.Value.FileId
                );
                return SeedOutcome.Failed;
            }

            var repairVersion = AddSeedVersion(
                existingTemplate,
                definition,
                repairUpload.Value,
                layoutId,
                layoutVersionNumber
            );
            if (repairVersion.IsFailure)
            {
                logger.LogError(
                    "Failed to create repaired version for template '{TemplateKey}': {Error}",
                    definition.TemplateKey,
                    repairVersion.Error.Message
                );
                return SeedOutcome.Failed;
            }

            existingTemplate.PublishVersion(repairVersion.Value.Id, PlatformTenant.Id, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Repaired and published template '{TemplateKey}'.", definition.TemplateKey);
            return SeedOutcome.Seeded;
        }

        var uploadResult = await storageService.UploadAsync(
            tenantId: null,
            definition.Channel == TemplateChannel.Push ? TemplateArtifactKind.Text : TemplateArtifactKind.Html,
            Encoding.UTF8.GetBytes(definition.Channel == TemplateChannel.Push ? definition.Text! : definition.Html),
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
            return SeedOutcome.Failed;
        }

        if (!await WaitUntilDownloadableAsync(storageService, uploadResult.Value.FileId, ct))
        {
            logger.LogError(
                "CloudStorage did not catalogue template '{TemplateKey}' file {FileId} in time.",
                definition.TemplateKey,
                uploadResult.Value.FileId
            );
            return SeedOutcome.Failed;
        }

        var templateResult = EmailTemplate.CreateNew(
            TemplateScope.System,
            null,
            templateKey,
            definition.Name,
            null,
            PlatformTenant.Id,
            DateTime.UtcNow,
            definition.Channel
        );
        if (templateResult.IsFailure)
        {
            logger.LogError(
                "Failed to create template '{TemplateKey}': {Error}",
                definition.TemplateKey,
                templateResult.Error.Message
            );
            return SeedOutcome.Failed;
        }

        var template = templateResult.Value;
        var versionResult = AddSeedVersion(template, definition, uploadResult.Value, layoutId, layoutVersionNumber);
        if (versionResult.IsFailure)
        {
            logger.LogError(
                "Failed to add draft version to template '{TemplateKey}': {Error}",
                definition.TemplateKey,
                versionResult.Error.Message
            );
            return SeedOutcome.Failed;
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
            return SeedOutcome.Failed;
        }
        await dbContext.EventTemplateMappings.AddAsync(mappingResult.Value, ct);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Seeded and published template '{TemplateKey}' mapped from event '{EventKey}'.",
            definition.TemplateKey,
            definition.EventKey
        );
        return SeedOutcome.Seeded;
    }

    private static BuildingBlocks.Results.Result<EmailTemplateVersion> AddSeedVersion(
        EmailTemplate template,
        NotificationTemplateSeed definition,
        TemplateStorageUpload upload,
        Guid layoutId,
        int layoutVersionNumber
    ) =>
        definition.Channel == TemplateChannel.Push
            ? template.AddPushDraftVersion(
                definition.Subject,
                upload.StorageKey,
                upload.FileId,
                definition.Variables,
                DateTime.UtcNow,
                definition.ContentVersion
            )
            : template.AddDraftVersion(
                definition.Subject,
                upload.StorageKey,
                upload.FileId,
                null,
                null,
                null,
                null,
                null,
                null,
                layoutId,
                layoutVersionNumber,
                NotificationTemplateSeedSource.VariablesWithPreheader(definition),
                DateTime.UtcNow,
                definition.ContentVersion
            );

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
