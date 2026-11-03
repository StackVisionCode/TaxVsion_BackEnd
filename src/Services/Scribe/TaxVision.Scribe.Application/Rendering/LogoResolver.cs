using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Resuelve qué logo embeber en un correo. System consulta SystemAssetRef (sembrado por
/// ScribeSystemAssetSeeder desde un archivo local al arrancar — ya no config estática). Tenant
/// consulta TenantLogoRef según la audiencia: Staff usa el del CRM; Client usa el del Portal y cae al
/// del CRM. Si no hay uno activo, cae al logo de plataforma con IsFallback=true y deja una nota interna
/// deduplicada (a lo sumo 1 por tenant por día — ver TenantLogoMissingNotification).
/// Si el logo de plataforma tampoco está sembrado todavía (recién
/// arrancó, o el seeder falló), devuelve <see cref="Guid.Empty"/> — el caller (FluidTemplateRenderer)
/// debe tratar eso como "sin logo" y omitir el inline asset, nunca bloquear el envío por esto.
/// Cache L1 5min, key por tenant y audiencia (ver <see cref="LogoCacheKeys"/>).
/// </summary>
public sealed class LogoResolver(
    ITenantLogoRefRepository logoRefRepository,
    ITenantLogoMissingNotificationRepository notificationRepository,
    ISystemAssetRefRepository systemAssetRefRepository,
    IMemoryCache l1Cache,
    IUnitOfWork unitOfWork,
    ILogger<LogoResolver> logger
) : ILogoResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<LogoAsset> ResolveAsync(
        LogoScope logoScope,
        Guid? tenantId,
        RenderAudience audience = RenderAudience.Staff,
        CancellationToken ct = default
    )
    {
        var tenantScoped = logoScope == LogoScope.Tenant && tenantId is not null;
        var cacheKey = tenantScoped ? LogoCacheKeys.Tenant(tenantId!.Value, audience) : LogoCacheKeys.System;
        if (l1Cache.TryGetValue<LogoAsset>(cacheKey, out var cached) && cached is not null)
            return cached;

        var resolved = tenantScoped
            ? await ResolveTenantLogoAsync(tenantId!.Value, audience, ct)
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

    // El logo va embebido inline por Content-ID en el correo y los clientes de email NO renderizan
    // SVG. El brand del tenant permite image/svg+xml (válido para el CRM/Portal en el navegador),
    // así que un logo SVG llegaría acá y saldría roto en el correo — solo raster es email-safe.
    private static readonly HashSet<string> EmailSafeRasterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
    };

    private async Task<LogoAsset> ResolveTenantLogoAsync(Guid tenantId, RenderAudience audience, CancellationToken ct)
    {
        var logos = await logoRefRepository.ListByTenantAsync(tenantId, ct);
        var hasActiveLogo = false;
        foreach (var surface in TenantLogoSurfaces.PreferenceFor(audience))
        {
            var logoRef = logos.FirstOrDefault(l => l.Surface == surface && l.IsActive);
            if (logoRef is null)
                continue;

            hasActiveLogo = true;
            if (EmailSafeRasterTypes.Contains(logoRef.ContentType))
                return new LogoAsset(
                    logoRef.CloudStorageFileId,
                    logoRef.ContentType,
                    logoRef.SizeBytes,
                    IsFallback: false
                );

            logger.LogWarning(
                "Tenant {TenantId} {Surface} logo content-type '{ContentType}' is not email-safe (email clients do not render SVG); trying the next option.",
                tenantId,
                surface,
                logoRef.ContentType
            );
        }

        // El tenant SÍ tiene logo, solo que no es usable en email (SVG) → cae al logo de plataforma.
        // No dispara la nota de "logo faltante": no es que falte, es incompatible.
        if (!hasActiveLogo)
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

        logger.LogInformation("Tenant {TenantId} has no active logo; falling back to the system logo.", tenantId);
    }
}
