using BuildingBlocks.Messaging.ScribeIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;
using Wolverine;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Resuelve qué logo embeber en un correo. System consulta SystemAssetRef (sembrado por
/// ScribeSystemAssetSeeder desde un archivo local al arrancar — ya no config estática). Tenant
/// consulta TenantLogoRef; si no hay uno activo, cae al logo de plataforma con IsFallback=true y
/// dispara ScribeTenantLogoMissingDetectedIntegrationEvent (a lo sumo 1 por tenant por día — ver
/// TenantLogoMissingNotification). Si el logo de plataforma tampoco está sembrado todavía (recién
/// arrancó, o el seeder falló), devuelve <see cref="Guid.Empty"/> — el caller (FluidTemplateRenderer)
/// debe tratar eso como "sin logo" y omitir el inline asset, nunca bloquear el envío por esto.
/// Cache L1 5min, key "logo:{tenantId?}".
/// </summary>
public sealed class LogoResolver(
    ITenantLogoRefRepository logoRefRepository,
    ITenantLogoMissingNotificationRepository notificationRepository,
    ISystemAssetRefRepository systemAssetRefRepository,
    IMemoryCache l1Cache,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    ILogger<LogoResolver> logger
) : ILogoResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<LogoAsset> ResolveAsync(LogoScope logoScope, Guid? tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"logo:{(logoScope == LogoScope.Tenant ? tenantId : null)?.ToString() ?? "system"}";
        if (l1Cache.TryGetValue<LogoAsset>(cacheKey, out var cached) && cached is not null)
            return cached;

        var resolved =
            logoScope == LogoScope.Tenant && tenantId is not null
                ? await ResolveTenantLogoAsync(tenantId.Value, ct)
                : await SystemLogoAsync(ct);

        l1Cache.Set(
            cacheKey,
            resolved,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl, Size = 1 }
        );
        return resolved;
    }

    private async Task<LogoAsset> SystemLogoAsync(CancellationToken ct)
    {
        var asset = await systemAssetRefRepository.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
        if (asset is not null)
            return new LogoAsset(asset.CloudStorageFileId, asset.ContentType, asset.SizeBytes, IsFallback: false);

        logger.LogWarning(
            "System header logo is not seeded yet (SystemAssetRef '{Key}' missing); emails will render without a logo.",
            SystemAssetKeys.HeaderLogo
        );
        return new LogoAsset(Guid.Empty, string.Empty, 0, IsFallback: true);
    }

    private async Task<LogoAsset> ResolveTenantLogoAsync(Guid tenantId, CancellationToken ct)
    {
        var logoRef = await logoRefRepository.GetByTenantIdAsync(tenantId, ct);
        if (logoRef is not null && logoRef.IsActive)
            return new LogoAsset(logoRef.CloudStorageFileId, logoRef.ContentType, logoRef.SizeBytes, IsFallback: false);

        await NotifyMissingLogoAsync(tenantId, ct);

        // IsFallback siempre true acá: significa "este tenant no tiene su propio logo", no
        // "el logo de plataforma está configurado" — eso lo decide SystemLogoAsync para su propio
        // caso (render System-scope). Si además el logo de plataforma tampoco está sembrado
        // (CloudStorageFileId == Guid.Empty), FluidTemplateRenderer ya omite el inline asset solo.
        var systemLogo = await SystemLogoAsync(ct);
        return systemLogo with { IsFallback = true };
    }

    private async Task NotifyMissingLogoAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var notification = await notificationRepository.GetByTenantIdAsync(tenantId, ct);
        if (notification is not null && notification.AlreadyNotifiedOn(now))
            return;

        if (notification is null)
            await notificationRepository.AddAsync(TenantLogoMissingNotification.Create(tenantId, now), ct);
        else
            notification.Touch(now);

        await unitOfWork.SaveChangesAsync(ct);

        await messageBus.PublishAsync(
            new ScribeTenantLogoMissingDetectedIntegrationEvent { TenantId = tenantId, DetectedAtUtc = now }
        );

        logger.LogInformation("Tenant {TenantId} has no active logo; falling back to the system logo.", tenantId);
    }
}
