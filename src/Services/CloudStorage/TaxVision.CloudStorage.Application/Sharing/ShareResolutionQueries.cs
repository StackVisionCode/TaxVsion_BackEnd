using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

public enum ShareAccessOutcome
{
    Redirect,
    PasswordRequired,
    Denied,
}

/// <summary>Nunca distingue "no existe" de "revocado/expirado/agotado/sin permiso" — todo colapsa a Denied (anti-enumeracion).</summary>
public sealed record ShareAccessResult(ShareAccessOutcome Outcome, string? PresignedUrl)
{
    public static ShareAccessResult Denied() => new(ShareAccessOutcome.Denied, null);

    public static ShareAccessResult NeedsPassword() => new(ShareAccessOutcome.PasswordRequired, null);

    public static ShareAccessResult Redirect(string url) => new(ShareAccessOutcome.Redirect, url);
}

/// <summary>
/// Fase C3 — endpoint publico (sin autenticacion). Sirve Visibility.Public directo,
/// y Visibility.ExternalRecipients cuando RecipientEmail coincide con uno de los
/// recipients del link (verificacion minima sin requerir cuenta). Cualquier otra
/// visibilidad se resuelve solo por ResolvePrivateShareHandler.
///
/// Fase C4 — cuando el link es de tipo Folder, FileId es obligatorio: identifica
/// cual archivo dentro del arbol se quiere servir (ver FolderShareCoverage).
/// </summary>
public sealed record ResolvePublicShareQuery(
    string Token,
    string? Password,
    string? RecipientEmail,
    Guid? FileId,
    string? Ip,
    string? UserAgent
);

public static class ResolvePublicShareHandler
{
    public static async Task<ShareAccessResult> Handle(
        ResolvePublicShareQuery query,
        IShareLinkRepository shares,
        IFileObjectRepository files,
        IFolderRepository folders,
        IObjectStorage storage,
        IShareLinkPasswordHasher passwordHasher,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = clock.UtcNow;
        var link = await shares.GetByTokenHashAsync(ShareToken.HashOf(query.Token), ct);
        if (link is null)
        {
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, null, "public", "TokenNotFound", ct);
            return ShareAccessResult.Denied();
        }
        if (!link.IsUsable(now))
        {
            await DenyAndSignalAsync(
                link,
                "NotUsable",
                now,
                audit,
                bus,
                query.Ip,
                query.UserAgent,
                unitOfWork,
                clock,
                ct
            );
            return ShareAccessResult.Denied();
        }
        if (!IsServedByPublicEndpoint(link, query))
        {
            await DenyAndSignalAsync(
                link,
                "VisibilityNotPublic",
                now,
                audit,
                bus,
                query.Ip,
                query.UserAgent,
                unitOfWork,
                clock,
                ct
            );
            return ShareAccessResult.Denied();
        }

        if (link.PasswordHash is { } passwordHash)
        {
            if (string.IsNullOrEmpty(query.Password))
                return ShareAccessResult.NeedsPassword();
            if (!passwordHasher.Verify(query.Password, passwordHash))
            {
                AuditDenied(link, null, query.Ip, query.UserAgent, audit, clock);
                await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "public", "WrongPassword", ct);
                await unitOfWork.SaveChangesAsync(ct);
                return ShareAccessResult.Denied();
            }
        }

        var file = await ShareLinkResourceResolver.ResolveFileAsync(link, query.FileId, files, folders, ct);
        if (file is null || file.Status != FileStatus.Available)
        {
            AuditDenied(link, null, query.Ip, query.UserAgent, audit, clock);
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "public", "ResourceNotAvailable", ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ShareAccessResult.Denied();
        }

        var disposition = ShareLinkResolutionSignals.ContentDispositionFor(link.Permission, file.OriginalName);
        var url = await storage.PresignGetAsync(
            options.Value.MainBucket,
            file.ObjectKey,
            TimeSpan.FromMinutes(2),
            disposition,
            ct
        );
        link.RegisterAccess(now);
        AuditAccessed(link, file.Id, query.Ip, query.UserAgent, audit, clock);
        await ShareLinkResolutionSignals.PublishAccessedAsync(bus, link, file.Id, "public", ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ShareAccessResult.Redirect(url.ToString());
    }

    private static async Task DenyAndSignalAsync(
        ShareLink link,
        string reason,
        DateTime now,
        IStorageAuditRepository audit,
        IMessageBus bus,
        string? ip,
        string? userAgent,
        IUnitOfWork unitOfWork,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        AuditDenied(link, null, ip, userAgent, audit, clock);
        await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "public", reason, ct);
        if (reason == "NotUsable" && link.IsExpired(now) && link.Status == ShareStatus.Active)
            await ShareLinkResolutionSignals.PublishExpiredAsync(bus, link, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static bool IsServedByPublicEndpoint(ShareLink link, ResolvePublicShareQuery query) =>
        link.Visibility switch
        {
            ShareVisibility.Public => true,
            ShareVisibility.ExternalRecipients => !string.IsNullOrWhiteSpace(query.RecipientEmail)
                && link.HasEmailRecipient(query.RecipientEmail),
            _ => false,
        };

    private static void AuditAccessed(
        ShareLink link,
        Guid fileId,
        string? ip,
        string? userAgent,
        IStorageAuditRepository audit,
        ISystemClock clock
    ) =>
        audit.Add(
            StorageAccessLog.Create(
                link.TenantId,
                fileId,
                link.CreatedByUserId,
                "share.accessed",
                "success",
                ip,
                userAgent,
                link.Id.ToString(),
                "channel=public",
                clock.UtcNow
            )
        );

    private static void AuditDenied(
        ShareLink link,
        Guid? fileId,
        string? ip,
        string? userAgent,
        IStorageAuditRepository audit,
        ISystemClock clock
    ) =>
        audit.Add(
            StorageAccessLog.Create(
                link.TenantId,
                fileId,
                link.CreatedByUserId,
                "share.access-denied",
                "forbidden",
                ip,
                userAgent,
                link.Id.ToString(),
                "channel=public",
                clock.UtcNow
            )
        );
}

/// <summary>
/// Fase C3 — endpoint privado [Authorize]: el token es necesario pero NO
/// suficiente. Fail-closed: el tenant del JWT debe coincidir con el tenant del
/// link, aunque el token en si sea valido — un actor de otro tenant con el token
/// correcto igual recibe Denied.
///
/// Fase C4 — cuando el link es de tipo Folder, FileId es obligatorio: identifica
/// cual archivo dentro del arbol se quiere servir (ver FolderShareCoverage).
/// </summary>
public sealed record ResolvePrivateShareQuery(
    string Token,
    Guid JwtTenantId,
    Guid JwtUserId,
    StorageActorScope Scope,
    Guid? FileId,
    string? Ip,
    string? UserAgent
);

public static class ResolvePrivateShareHandler
{
    public static async Task<ShareAccessResult> Handle(
        ResolvePrivateShareQuery query,
        IShareLinkRepository shares,
        IFileObjectRepository files,
        IFolderRepository folders,
        IObjectStorage storage,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = clock.UtcNow;
        var link = await shares.GetByTokenHashAsync(ShareToken.HashOf(query.Token), ct);
        if (link is null)
        {
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, null, "private", "TokenNotFound", ct);
            return ShareAccessResult.Denied();
        }
        if (!link.IsUsable(now) || link.Visibility == ShareVisibility.Public)
        {
            AuditDenied(link, query, audit, clock);
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "private", "NotUsable", ct);
            if (link.IsExpired(now) && link.Status == ShareStatus.Active)
                await ShareLinkResolutionSignals.PublishExpiredAsync(bus, link, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ShareAccessResult.Denied();
        }

        if (query.JwtTenantId != link.TenantId)
        {
            AuditDenied(link, query, audit, clock);
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "private", "TenantMismatch", ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ShareAccessResult.Denied();
        }

        if (!IsAuthorized(link, query))
        {
            AuditDenied(link, query, audit, clock);
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "private", "NotAuthorized", ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ShareAccessResult.Denied();
        }

        var file = await ShareLinkResourceResolver.ResolveFileAsync(link, query.FileId, files, folders, ct);
        if (file is null || file.Status != FileStatus.Available)
        {
            AuditDenied(link, query, audit, clock);
            await ShareLinkResolutionSignals.PublishAccessDeniedAsync(bus, link, "private", "ResourceNotAvailable", ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ShareAccessResult.Denied();
        }

        var disposition = ShareLinkResolutionSignals.ContentDispositionFor(link.Permission, file.OriginalName);
        var url = await storage.PresignGetAsync(
            options.Value.MainBucket,
            file.ObjectKey,
            TimeSpan.FromMinutes(2),
            disposition,
            ct
        );
        link.RegisterAccess(now);
        audit.Add(
            StorageAccessLog.Create(
                link.TenantId,
                file.Id,
                query.JwtUserId,
                "share.accessed",
                "success",
                query.Ip,
                query.UserAgent,
                link.Id.ToString(),
                "channel=private",
                clock.UtcNow
            )
        );
        await ShareLinkResolutionSignals.PublishAccessedAsync(bus, link, file.Id, "private", ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ShareAccessResult.Redirect(url.ToString());
    }

    private static bool IsAuthorized(ShareLink link, ResolvePrivateShareQuery query) =>
        link.Visibility switch
        {
            ShareVisibility.TenantOnly => true,
            ShareVisibility.SpecificUsers => link.HasUserRecipient(query.JwtUserId),
            ShareVisibility.TenantCustomers => IsAuthorizedCustomer(link, query.Scope),
            _ => false,
        };

    private static bool IsAuthorizedCustomer(ShareLink link, StorageActorScope scope) =>
        scope is { IsCustomerPortal: true, CustomerId: { } customerId }
        && (!link.HasAnyRecipient || link.HasCustomerRecipient(customerId));

    private static void AuditDenied(
        ShareLink link,
        ResolvePrivateShareQuery query,
        IStorageAuditRepository audit,
        ISystemClock clock
    ) =>
        audit.Add(
            StorageAccessLog.Create(
                link.TenantId,
                link.ResourceId,
                query.JwtUserId,
                "share.access-denied",
                "forbidden",
                query.Ip,
                query.UserAgent,
                link.Id.ToString(),
                "channel=private",
                clock.UtcNow
            )
        );
}

/// <summary>
/// Fase C4 — resuelve QUE file servir para un link ya autorizado: si el link es
/// de File, es el propio ResourceId; si es de Folder, el llamador debe indicar
/// cual FileId dentro del arbol quiere, validado por FolderShareCoverage.
/// Compartido por el resolver publico y el privado — misma regla en ambos.
/// </summary>
internal static class ShareLinkResourceResolver
{
    public static async Task<FileObject?> ResolveFileAsync(
        ShareLink link,
        Guid? requestedFileId,
        IFileObjectRepository files,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (link.ResourceType == ShareResourceType.File)
            return await files.GetAsync(link.TenantId, link.ResourceId, ct);

        if (requestedFileId is not { } fileId)
            return null;

        var file = await files.GetAsync(link.TenantId, fileId, ct);
        if (file is null || !await FolderShareCoverage.CoversAsync(link, file, folders, ct))
            return null;

        return file;
    }
}

/// <summary>
/// Fase C4 (completitud) — helpers compartidos por ResolvePublicShareHandler y
/// ResolvePrivateShareHandler para (1) mapear SharePermission a un
/// Content-Disposition real, y (2) publicar los eventos de integracion de
/// acceso/denegacion/expiracion (§31 del plan) sin duplicar la logica entre los
/// dos handlers. Ninguno de estos eventos cambia la respuesta HTTP al cliente —
/// esta siempre sigue siendo el mismo 404/302 generico (anti-enumeracion); son
/// solo para que un consumidor externo (analytics/SIEM) pueda reaccionar.
/// </summary>
internal static class ShareLinkResolutionSignals
{
    /// <summary>
    /// View/Preview -> inline (se renderiza en el browser, no fuerza descarga).
    /// Download -> attachment (fuerza el dialogo de "guardar como").
    /// Upload/EditMetadata no tienen un GET que servir por diseño (son verbos de
    /// escritura, no de lectura) — si de algun modo llegan hasta aca, inline es
    /// el fallback mas seguro (no fuerza una descarga no solicitada).
    /// </summary>
    public static string ContentDispositionFor(SharePermission permission, string originalName)
    {
        var kind = permission == SharePermission.Download ? "attachment" : "inline";
        var safeName = originalName.Replace('"', '_');
        return $"{kind}; filename=\"{safeName}\"";
    }

    public static ValueTask PublishAccessedAsync(
        IMessageBus bus,
        ShareLink link,
        Guid fileId,
        string channel,
        CancellationToken ct
    ) =>
        bus.PublishAsync(
            new ShareLinkAccessedIntegrationEvent
            {
                TenantId = link.TenantId,
                ShareLinkId = link.Id,
                FileId = fileId,
                Channel = channel,
                CorrelationId = link.Id.ToString(),
            }
        );

    /// <summary>
    /// link puede ser null (token que no resuelve a ningun ShareLink) — en ese
    /// caso no hay TenantId real; se usa Guid.Empty solo para el campo heredado
    /// de IntegrationEvent, ningun consumidor deberia filtrar por el en ese caso
    /// (Reason="TokenNotFound" es la senal a mirar).
    /// </summary>
    public static ValueTask PublishAccessDeniedAsync(
        IMessageBus bus,
        ShareLink? link,
        string channel,
        string reason,
        CancellationToken ct
    ) =>
        bus.PublishAsync(
            new ShareLinkAccessDeniedIntegrationEvent
            {
                TenantId = link?.TenantId ?? Guid.Empty,
                ShareLinkId = link?.Id,
                Channel = channel,
                Reason = reason,
                CorrelationId = link?.Id.ToString() ?? string.Empty,
            }
        );

    public static ValueTask PublishExpiredAsync(IMessageBus bus, ShareLink link, CancellationToken ct) =>
        bus.PublishAsync(
            new ShareLinkExpiredIntegrationEvent
            {
                TenantId = link.TenantId,
                ShareLinkId = link.Id,
                ResourceId = link.ResourceId,
                ExpiresAtUtc = link.ExpiresAtUtc,
                CorrelationId = link.Id.ToString(),
            }
        );
}
