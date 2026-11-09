using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections;

/// <summary>
/// Mantiene las proyecciones de la oficina que usan el certificado y la vista pública de firma:
/// <see cref="TenantBrandingRef"/> (nombre y subdominio: TenantCreated, TenantUpdated,
/// TenantSubdomainChanged; nombre comercial: TenantProfileChanged) y <see cref="TenantLogoRef"/> (un logo por superficie: TenantLogoUpdated /
/// TenantLogoRemoved). Todo es idempotente y un evento más viejo que lo proyectado se ignora; el retiro
/// del logo de una superficie no toca la otra.
/// </summary>
public static class TenantBrandingProjectionConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var now = DateTime.UtcNow;
            var existing = await repository.GetByTenantIdAsync(evt.NewTenantId, ct);
            if (existing is null)
            {
                var branding = TenantBrandingRef.Create(evt.NewTenantId, now);
                branding.ApplyOfficeName(evt.Name, evt.OccurredOn, now);
                branding.SetSubDomain(evt.SubDomain, now);
                await repository.AddAsync(branding, ct);
            }
            else
            {
                // Un TenantCreated repetido no pisa un renombre posterior.
                existing.ApplyOfficeName(evt.Name, evt.OccurredOn, now);
                existing.SetSubDomain(evt.SubDomain, now);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("TenantBrandingRef office name upserted for tenant {TenantId}.", evt.NewTenantId);
        }
    }

    /// <summary>La oficina se renombró: el certificado debe salir con el nombre actual.</summary>
    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var now = DateTime.UtcNow;
            var existing = await repository.GetByTenantIdAsync(evt.UpdatedTenantId, ct);
            if (existing is null)
            {
                var branding = TenantBrandingRef.Create(evt.UpdatedTenantId, now);
                branding.ApplyOfficeName(evt.Name, evt.UpdatedAtUtc, now);
                await repository.AddAsync(branding, ct);
            }
            else if (!existing.ApplyOfficeName(evt.Name, evt.UpdatedAtUtc, now))
            {
                logger.LogInformation(
                    "TenantUpdated ignored for tenant {TenantId}: older than the projected office name.",
                    evt.UpdatedTenantId
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("TenantBrandingRef office name updated for tenant {TenantId}.", evt.UpdatedTenantId);
        }
    }

    /// <summary>
    /// La oficina guardó su perfil: el certificado sale con el nombre comercial (DisplayName ?? OfficeName).
    /// Se ordena por la versión del perfil, independiente de la fecha del renombre.
    /// </summary>
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var now = DateTime.UtcNow;
            var existing = await repository.GetByTenantIdAsync(evt.ProfileTenantId, ct);
            if (existing is null)
            {
                // El nombre y el subdominio los completa TenantCreated o el backfill.
                var branding = TenantBrandingRef.Create(evt.ProfileTenantId, now);
                branding.ApplyProfile(evt.Version, evt.DisplayName, now);
                await repository.AddAsync(branding, ct);
            }
            else if (!existing.ApplyProfile(evt.Version, evt.DisplayName, now))
            {
                logger.LogInformation(
                    "TenantProfileChanged v{Version} ignored for tenant {TenantId}: already at v{ProfileVersion}.",
                    evt.Version,
                    evt.ProfileTenantId,
                    existing.ProfileVersion
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "TenantBrandingRef profile v{Version} applied for tenant {TenantId}.",
                evt.Version,
                evt.ProfileTenantId
            );
        }
    }

    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            if (!TryParseSurface(evt.Surface, evt.TenantId, logger, out var surface))
                return;

            var existing = await repository.GetLogoAsync(evt.TenantId, surface, ct);
            if (existing is null)
            {
                var logo = TenantLogoRef.Create(evt.TenantId, surface, evt.UpdatedAtUtc);
                logo.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.SizeBytes, evt.UpdatedAtUtc);
                await repository.AddLogoAsync(logo, ct);
            }
            else if (!existing.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.SizeBytes, evt.UpdatedAtUtc))
            {
                logger.LogInformation(
                    "TenantLogoUpdated ignored for tenant {TenantId} {Surface}: older than the projected logo.",
                    evt.TenantId,
                    surface
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "TenantLogoRef upserted for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    /// <summary>El logo se borró en Tenant: deja de salir en los certificados (solo esa superficie).</summary>
    public static async Task Handle(
        TenantLogoRemovedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            if (!TryParseSurface(evt.Surface, evt.TenantId, logger, out var surface))
                return;

            var existing = await repository.GetLogoAsync(evt.TenantId, surface, ct);
            if (existing is null)
                // Lápida: una fila sin logo con la fecha del retiro frena al Updated más viejo.
                await repository.AddLogoAsync(TenantLogoRef.Create(evt.TenantId, surface, evt.RemovedAtUtc), ct);
            else if (!existing.RemoveLogo(evt.RemovedAtUtc))
                return;

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "TenantLogoRef removed for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    /// <summary>
    /// La oficina renombró su subdominio primario: la proyección guarda el slug que se le devuelve a la
    /// página pública de firma. Sin fila todavía no hay nada que renombrar (la crea TenantCreated).
    /// </summary>
    public static async Task Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        if (!evt.IsPrimary)
            return;

        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetByTenantIdAsync(evt.TenantId, ct);
            if (existing is null || existing.SubDomain == evt.NewSlug)
                return;

            existing.SetSubDomain(evt.NewSlug, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("TenantBrandingRef subdomain renamed for tenant {TenantId}.", evt.TenantId);
        }
    }

    private static bool TryParseSurface(string? value, Guid tenantId, ILogger logger, out TenantLogoSurface surface)
    {
        if (TenantLogoSurfaces.TryParse(value, out surface))
            return true;

        logger.LogWarning("Tenant logo event ignored for {TenantId}: unknown surface '{Surface}'.", tenantId, value);
        return false;
    }

    private static string ResolveCorrelationId(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
