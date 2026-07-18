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
        var alreadySeeded = await dbContext.EmailLayouts.AnyAsync(
            l => l.Scope == TemplateScope.System && l.LayoutKey == layoutKey,
            ct
        );
        if (alreadySeeded)
            return;

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
