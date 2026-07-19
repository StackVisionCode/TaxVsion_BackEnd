using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>
/// Sube el logo de header de la plataforma a CloudStorage al arrancar, leyéndolo de un archivo
/// local (Assets/SystemLogo/deploy.png, copiado al output de publish vía el .csproj) y persiste el
/// FileId resultante en SystemAssetRef — reemplaza la config estática Scribe:SystemAssets, que
/// requería copiar un GUID a mano y quedaba desincronizada. Mismo patrón que
/// <see cref="ScribeBaseLayoutSeeder"/>/<see cref="ScribeNotificationTemplateSeeder"/>: corre después
/// de ApplicationStarted (Wolverine recién publica ahí), es idempotente (si el FileId ya guardado
/// sigue siendo descargable, no hace nada) y se auto-repara si CloudStorage perdió el archivo. Un
/// fallo acá NUNCA debe tumbar el arranque ni bloquear el envío de correos — LogoResolver ya
/// contempla que SystemAssetRef todavía no exista y FluidTemplateRenderer omite el logo inline en
/// ese caso, así que el peor caso es "los correos salen sin logo hasta que esto corra bien".
/// </summary>
public sealed class ScribeSystemAssetSeeder(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<ScribeSystemAssetSeeder> logger
) : IHostedService
{
    private const int DownloadableWaitAttempts = 180;
    private const string LocalFileName = "deploy.png";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = SeedAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        try
        {
            var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SystemLogo", LocalFileName);
            if (!File.Exists(localPath))
            {
                logger.LogInformation(
                    "ScribeSystemAssetSeeder: '{Path}' not found — skipping. Drop the logo file there and redeploy to seed it.",
                    localPath
                );
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISystemAssetRefRepository>();
            var storageService = scope.ServiceProvider.GetRequiredService<ITemplateStorageService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var bytes = await File.ReadAllBytesAsync(localPath, ct);
            var existing = await repository.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
            if (existing is not null && existing.SizeBytes == bytes.LongLength)
            {
                // Comparar tamaño (no hash — no vale la pena la complejidad para un archivo que
                // cambia a mano ocasionalmente) para detectar tanto "reemplazaron deploy.png por
                // uno nuevo" como "sigue siendo el mismo": solo re-sube si difiere.
                var download = await storageService.DownloadTextAsync(existing.CloudStorageFileId, null, ct);
                if (download.IsSuccess)
                {
                    logger.LogDebug("ScribeSystemAssetSeeder: header logo already seeded and downloadable, skipping.");
                    return;
                }

                logger.LogWarning(
                    "SystemAssetRef '{Key}' references missing CloudStorage file {FileId}; repairing it.",
                    SystemAssetKeys.HeaderLogo,
                    existing.CloudStorageFileId
                );
            }
            else if (existing is not null)
            {
                logger.LogInformation(
                    "ScribeSystemAssetSeeder: local '{FileName}' size changed ({OldSize} -> {NewSize} bytes); re-uploading.",
                    LocalFileName,
                    existing.SizeBytes,
                    bytes.LongLength
                );
            }

            var uploadResult = await storageService.UploadAsync(
                tenantId: null,
                TemplateArtifactKind.SystemLogo,
                bytes,
                PlatformTenant.Id,
                ct
            );
            if (uploadResult.IsFailure)
            {
                logger.LogError("Failed to upload system header logo: {Error}", uploadResult.Error.Message);
                return;
            }

            if (!await WaitUntilDownloadableAsync(storageService, uploadResult.Value.FileId, ct))
            {
                logger.LogError(
                    "CloudStorage did not catalogue system header logo file {FileId} in time.",
                    uploadResult.Value.FileId
                );
                return;
            }

            if (existing is not null)
                existing.Update(uploadResult.Value.FileId, "image/png", bytes.LongLength, DateTime.UtcNow);
            else
                await repository.AddAsync(
                    SystemAssetRef.Create(
                        SystemAssetKeys.HeaderLogo,
                        uploadResult.Value.FileId,
                        "image/png",
                        bytes.LongLength,
                        DateTime.UtcNow
                    ),
                    ct
                );

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "ScribeSystemAssetSeeder: seeded header logo (FileId {FileId}, {Bytes} bytes).",
                uploadResult.Value.FileId,
                bytes.LongLength
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed the system header logo on startup.");
        }
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
