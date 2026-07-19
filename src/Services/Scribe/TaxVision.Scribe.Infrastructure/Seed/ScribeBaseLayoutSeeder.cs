using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>
/// Sube y publica los layouts base "system-base"/"tenant-base" (HTML fijo en
/// <see cref="TaxVision.Scribe.Application.Templates.BaseLayouts.BaseLayoutHtml"/>, Fase 4.6) al
/// arrancar si todavía no existen — Fase 4.6 dejó esto pendiente porque el upload a CloudStorage
/// (TemplateStorageService) recién existe desde Fase 5. Usa <see cref="PlatformTenant.Id"/> como actor:
/// no hay un usuario real detrás de un seed de arranque.
/// </summary>
public sealed class ScribeBaseLayoutSeeder(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<ScribeBaseLayoutSeeder> logger
) : IHostedService
{
    // CloudStorage scans every uploaded asset with ClamAV. Its configured timeout is
    // 120 seconds, so a 30-second polling window can abandon a healthy slow scan.
    private const int DownloadableWaitAttempts = 180;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // TemplateStorageService.UploadAsync publica un evento vía Wolverine, y Wolverine recién se
        // considera arrancado cuando TODO el host terminó StartAsync (ApplicationStarted) — publicar
        // acá directo tira WolverineHasNotStartedException. Se difiere el seed a ese punto.
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

            await SeedIfMissingAsync(
                dbContext,
                storageService,
                unitOfWork,
                "system-base",
                "System base layout",
                Application.Templates.BaseLayouts.BaseLayoutHtml.SystemBaseV1,
                cancellationToken
            );
            await SeedIfMissingAsync(
                dbContext,
                storageService,
                unitOfWork,
                "tenant-base",
                "Tenant base layout",
                Application.Templates.BaseLayouts.BaseLayoutHtml.TenantBaseV1,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed base layouts on startup.");
        }
    }

    private async Task SeedIfMissingAsync(
        ScribeDbContext dbContext,
        ITemplateStorageService storageService,
        IUnitOfWork unitOfWork,
        string layoutKeyValue,
        string name,
        string html,
        CancellationToken ct
    )
    {
        var layoutKey = LayoutKey.Create(layoutKeyValue).Value;
        var existingLayout = await dbContext
            .EmailLayouts.Include(l => l.Versions)
            .FirstOrDefaultAsync(l => l.Scope == TemplateScope.System && l.LayoutKey == layoutKey, ct);
        if (existingLayout is not null)
        {
            var publishedVersion = existingLayout
                .Versions.Where(v => v.Status == EmailVersionStatus.Published)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            if (publishedVersion is not null)
            {
                var download = await storageService.DownloadTextAsync(publishedVersion.HtmlFileId, null, ct);
                if (download.IsSuccess)
                    return;

                logger.LogWarning(
                    "Published base layout '{LayoutKey}' references missing CloudStorage file {FileId}; repairing it.",
                    layoutKeyValue,
                    publishedVersion.HtmlFileId
                );
            }

            var repairUpload = await storageService.UploadAsync(
                null,
                TemplateArtifactKind.Html,
                Encoding.UTF8.GetBytes(html),
                PlatformTenant.Id,
                ct
            );
            if (repairUpload.IsFailure)
            {
                logger.LogError(
                    "Failed to repair base layout '{LayoutKey}': {Error}",
                    layoutKeyValue,
                    repairUpload.Error.Message
                );
                return;
            }

            if (!await WaitUntilDownloadableAsync(storageService, repairUpload.Value.FileId, ct))
            {
                logger.LogError(
                    "CloudStorage did not catalogue repaired base layout '{LayoutKey}' file {FileId} in time.",
                    layoutKeyValue,
                    repairUpload.Value.FileId
                );
                return;
            }

            var repairVersion = existingLayout.AddDraftVersion(
                repairUpload.Value.StorageKey,
                repairUpload.Value.FileId,
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            );
            if (repairVersion.IsFailure)
            {
                logger.LogError(
                    "Failed to create repaired version for base layout '{LayoutKey}': {Error}",
                    layoutKeyValue,
                    repairVersion.Error.Message
                );
                return;
            }

            existingLayout.PublishVersion(repairVersion.Value.Id, PlatformTenant.Id, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Repaired and published base layout '{LayoutKey}'.", layoutKeyValue);
            return;
        }

        var uploadResult = await storageService.UploadAsync(
            tenantId: null,
            TemplateArtifactKind.Html,
            Encoding.UTF8.GetBytes(html),
            PlatformTenant.Id,
            ct
        );
        if (uploadResult.IsFailure)
        {
            logger.LogError(
                "Failed to upload base layout '{LayoutKey}': {Error}",
                layoutKeyValue,
                uploadResult.Error.Message
            );
            return;
        }

        if (!await WaitUntilDownloadableAsync(storageService, uploadResult.Value.FileId, ct))
        {
            logger.LogError(
                "CloudStorage did not catalogue base layout '{LayoutKey}' file {FileId} in time.",
                layoutKeyValue,
                uploadResult.Value.FileId
            );
            return;
        }

        var layoutResult = EmailLayout.CreateNew(
            TemplateScope.System,
            null,
            layoutKey,
            name,
            null,
            PlatformTenant.Id,
            DateTime.UtcNow
        );
        if (layoutResult.IsFailure)
        {
            logger.LogError(
                "Failed to create base layout '{LayoutKey}': {Error}",
                layoutKeyValue,
                layoutResult.Error.Message
            );
            return;
        }

        var layout = layoutResult.Value;
        var versionResult = layout.AddDraftVersion(
            uploadResult.Value.StorageKey,
            uploadResult.Value.FileId,
            null,
            null,
            null,
            null,
            DateTime.UtcNow
        );
        if (versionResult.IsFailure)
        {
            logger.LogError(
                "Failed to add draft version to base layout '{LayoutKey}': {Error}",
                layoutKeyValue,
                versionResult.Error.Message
            );
            return;
        }

        layout.PublishVersion(versionResult.Value.Id, PlatformTenant.Id, DateTime.UtcNow);

        await dbContext.EmailLayouts.AddAsync(layout, ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Seeded and published base layout '{LayoutKey}'.", layoutKeyValue);
    }

    private static async Task<bool> WaitUntilDownloadableAsync(
        ITemplateStorageService storageService,
        Guid fileId,
        CancellationToken ct
    )
    {
        for (var attempt = 1; attempt <= DownloadableWaitAttempts; attempt++)
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
