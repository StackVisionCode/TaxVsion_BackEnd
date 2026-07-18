using BuildingBlocks.Messaging.ScribeIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;
using Wolverine;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Resuelve qué logo embeber en un correo. System siempre devuelve el logo de plataforma
/// (config). Tenant consulta TenantLogoRef; si no hay uno activo, cae al logo de plataforma con
/// IsFallback=true y dispara ScribeTenantLogoMissingDetectedIntegrationEvent (a lo sumo 1 por
/// tenant por día — ver TenantLogoMissingNotification). Cache L1 5min, key "logo:{tenantId?}".
/// </summary>
public sealed class LogoResolver(
    ITenantLogoRefRepository logoRefRepository,
    ITenantLogoMissingNotificationRepository notificationRepository,
    IOptions<SystemAssetsOptions> systemAssets,
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
                : SystemLogo();

        l1Cache.Set(
            cacheKey,
            resolved,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl, Size = 1 }
        );
        return resolved;
    }

    private LogoAsset SystemLogo()
    {
        var opt = systemAssets.Value;
        return new LogoAsset(
            opt.HeaderLogoFileId,
            opt.HeaderLogoContentType,
            opt.HeaderLogoSizeBytes,
            IsFallback: false
        );
    }

    private async Task<LogoAsset> ResolveTenantLogoAsync(Guid tenantId, CancellationToken ct)
    {
        var logoRef = await logoRefRepository.GetByTenantIdAsync(tenantId, ct);
        if (logoRef is not null && logoRef.IsActive)
            return new LogoAsset(logoRef.CloudStorageFileId, logoRef.ContentType, logoRef.SizeBytes, IsFallback: false);

        await NotifyMissingLogoAsync(tenantId, ct);

        var opt = systemAssets.Value;
        return new LogoAsset(
            opt.HeaderLogoFileId,
            opt.HeaderLogoContentType,
            opt.HeaderLogoSizeBytes,
            IsFallback: true
        );
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
