using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Recurso al que lleva un push: el mismo que el in-app del consumer. <see cref="SourceEventId"/> solo cuando
/// el aviso crea su propia fila en la bandeja (el clic la busca por ese id).
/// </summary>
public sealed record PushTarget(
    string Kind,
    string? ResourceId = null,
    Guid? CustomerId = null,
    Guid? SourceEventId = null
);

/// <summary>Contrato único de <c>Data</c> y <c>Link</c> del push: sirve a la web de hoy y a una app móvil.</summary>
public static class PushPayloads
{
    /// <summary>Nombre de contrato del recurso del kind; null si el kind no está en el catálogo.</summary>
    public static string? ResourceTypeOf(string kind) =>
        NotificationCatalog.TryGet(kind, out var entry) && entry is not null
            ? NotificationContractNames.For(entry.ResourceType)
            : null;

    /// <summary><c>{base}/n/open?rt=&amp;rid=&amp;cid=&amp;ev=</c>, omitiendo lo vacío. Solo ids.</summary>
    public static string Link(string baseUrl, PushTarget target)
    {
        var query = new List<string>(4);
        if (ResourceTypeOf(target.Kind) is { } resourceType)
            query.Add($"rt={resourceType}");
        if (!string.IsNullOrWhiteSpace(target.ResourceId))
            query.Add($"rid={Uri.EscapeDataString(target.ResourceId)}");
        if (target.CustomerId is { } customerId)
            query.Add($"cid={customerId:D}");
        if (target.SourceEventId is { } sourceEventId)
            query.Add($"ev={sourceEventId:D}");
        var path = $"{baseUrl.TrimEnd('/')}/n/open";
        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    /// <summary>Claves comunes del push; las propias del emisor (<paramref name="extra"/>) se conservan.</summary>
    public static Dictionary<string, string> Data(
        PushTarget target,
        Guid tenantId,
        Guid recipientUserId,
        string? link,
        IReadOnlyDictionary<string, string>? extra = null
    )
    {
        var data = extra is null ? new Dictionary<string, string>() : new Dictionary<string, string>(extra);
        data["kind"] = target.Kind;
        if (ResourceTypeOf(target.Kind) is { } resourceType)
            data["resourceType"] = resourceType;
        if (!string.IsNullOrWhiteSpace(target.ResourceId))
            data["resourceId"] = target.ResourceId;
        if (target.CustomerId is { } customerId)
            data["customerId"] = customerId.ToString();
        data["tenantId"] = tenantId.ToString();
        data["recipientUserId"] = recipientUserId.ToString();
        if (target.SourceEventId is { } sourceEventId)
            data["sourceEventId"] = sourceEventId.ToString();
        if (!string.IsNullOrWhiteSpace(link))
            data["link"] = link;
        return data;
    }

    /// <summary>
    /// Petición de push con datos y enlace. Sin <paramref name="baseUrl"/> no hay enlace; con
    /// <paramref name="link"/> se respeta el enlace propio del emisor.
    /// </summary>
    public static PushDispatchRequest Request(
        Guid tenantId,
        Guid recipientUserId,
        string title,
        string body,
        NotificationCategory category,
        PushTarget target,
        Guid? relatedEventId,
        string? correlationId,
        string? baseUrl,
        IReadOnlyDictionary<string, string>? extraData = null,
        string? tag = null,
        string? link = null,
        bool skipWhenNoDevices = false,
        TimeSpan? timeToLive = null
    )
    {
        var url = link ?? (baseUrl is null ? null : Link(baseUrl, target));
        return new PushDispatchRequest(
            tenantId,
            recipientUserId,
            title,
            body,
            category,
            target.Kind,
            target.Kind,
            relatedEventId,
            correlationId,
            Data(target, tenantId, recipientUserId, url, extraData),
            Tag: tag,
            Link: url,
            SkipWhenNoDevices: skipWhenNoDevices,
            TimeToLive: timeToLive
        );
    }
}

/// <summary>Base del enlace del push: CRM para el staff y portal para las cuentas de cliente.</summary>
public sealed class PushLinks(
    ITenantHostResolver hosts,
    IOptions<PortalOptions> portal,
    ICustomerPortalAccountProjectionRepository portalAccounts
)
{
    public async Task<string> StaffBaseAsync(Guid tenantId, CancellationToken ct) =>
        TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(tenantId, ct), portal.Value);

    public async Task<string> ClientBaseAsync(Guid tenantId, CancellationToken ct) =>
        TenantEmailLinks.ClientBase(await hosts.ResolveHostAsync(tenantId, ct), portal.Value);

    /// <summary>Para chat y llamadas, donde el destinatario puede ser staff o cliente.</summary>
    public async Task<string> BaseForUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        await portalAccounts.GetByUserAsync(tenantId, userId, ct) is { IsActive: true }
            ? await ClientBaseAsync(tenantId, ct)
            : await StaffBaseAsync(tenantId, ct);
}
