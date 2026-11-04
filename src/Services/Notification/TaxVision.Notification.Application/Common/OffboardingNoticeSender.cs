using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>Lo común a todos los destinatarios de un aviso del retiro de un empleado.</summary>
/// <param name="DeliveryKey">
/// Hecho de negocio del aviso (p. ej. <c>connection:{id}:{retirado}</c>). Si viene, correo y push dedupan
/// por él y no por <c>EventId</c>: el productor puede volver a publicar el mismo aviso con otro EventId.
/// Null = por evento (cada evento es un hecho nuevo).
/// </param>
public sealed record OffboardingNoticeContext(
    Guid TenantId,
    Guid EventId,
    DateTime OccurredOn,
    string SourceEventType,
    string Kind,
    NotificationCategory Category,
    string EmailEventKey,
    string Link,
    string? ResourceId,
    string GroupKey,
    IReadOnlyDictionary<string, string>? ResourceContext = null,
    string? DeliveryKey = null
)
{
    /// <summary>Id de dedupe de correo y push: estable por <see cref="DeliveryKey"/>, o el del evento.</summary>
    public Guid DeliveryId =>
        string.IsNullOrWhiteSpace(DeliveryKey) ? EventId : StableId($"{Kind}|{TenantId:N}|{DeliveryKey}");

    // Mismo texto = mismo Guid (SHA-256 truncado); el Kind y el tenant evitan choques entre avisos.
    private static Guid StableId(string material) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(material)).AsSpan(0, 16));
}

/// <summary>Un texto (campana, push y correo) para un grupo de destinatarios ya resueltos.</summary>
public sealed record OffboardingNotice(
    IReadOnlyList<Guid> Recipients,
    string Title,
    string Body,
    IReadOnlyDictionary<string, object?> EmailVariables,
    IReadOnlyDictionary<string, string>? Params = null
);

/// <summary>
/// Entrega los avisos del retiro de empleados con el patrón de <c>OfficeWispReviewDueConsumer</c>: primero
/// todos los correos (Scribe es lo único que puede lanzar, así un reintento de Wolverine no repite campana
/// ni push) y después campana y push por destinatario. Correo y push dedupan por
/// <see cref="OffboardingNoticeContext.DeliveryId"/> y destinatario; la campana se agrupa por <c>GroupKey</c>.
/// Los inactivos los omiten el dispatcher y el resolvedor de correo.
/// </summary>
public sealed class OffboardingNoticeSender(
    NotificationDispatcher dispatcher,
    IRecipientResolver resolver,
    IUserEmailResolver emails,
    IUserNotificationPreferenceRepository preferences,
    IEmailDispatchGateway gateway,
    IScribeRenderClient scribe,
    ITenantHostResolver hosts,
    IOptions<PortalOptions> portal,
    ICorrelationContext correlation
)
{
    /// <summary>Enlace al CRM de la oficina (su subdominio, o el base fijo si no se resolvió).</summary>
    public async Task<string> StaffLinkAsync(Guid tenantId, string path, CancellationToken ct) =>
        $"{TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(tenantId, ct), portal.Value)}{path}";

    /// <summary>Los usuarios dados que siguen activos en el tenant (proyección local).</summary>
    public async Task<IReadOnlyList<Guid>> ActiveUsersAsync(
        Guid tenantId,
        IEnumerable<Guid> userIds,
        CancellationToken ct
    )
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return [];
        var resolved = await resolver.ResolveAsync(new Participants(tenantId, ids), RecipientValidation.None, ct);
        return resolved.IsSuccess ? resolved.Value : [];
    }

    /// <summary>Quienes tienen alguno de los permisos, sin repetidos.</summary>
    public async Task<IReadOnlyList<Guid>> ByPermissionsAsync(
        Guid tenantId,
        IEnumerable<string> permissions,
        CancellationToken ct
    )
    {
        var recipients = new List<Guid>();
        foreach (
            var permission in permissions
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal)
        )
        {
            var resolved = await resolver.ResolveAsync(
                new ByPermission(tenantId, permission),
                RecipientValidation.None,
                ct
            );
            if (resolved.IsSuccess)
                recipients.AddRange(resolved.Value);
        }
        return recipients.Distinct().ToArray();
    }

    public async Task SendAsync(
        OffboardingNoticeContext context,
        IReadOnlyList<OffboardingNotice> notices,
        CancellationToken ct
    )
    {
        foreach (var notice in notices)
            await SendEmailsAsync(context, notice, ct);

        var resourceType = NotificationContractNames.For(NotificationCatalog.GetRequired(context.Kind).ResourceType);
        foreach (var notice in notices)
        foreach (var userId in notice.Recipients)
        {
            await RecordInAppAsync(context, notice, userId, ct);
            await dispatcher.SendPushAsync(
                new PushDispatchRequest(
                    context.TenantId,
                    userId,
                    notice.Title,
                    notice.Body,
                    context.Category,
                    context.Kind,
                    context.Kind,
                    context.DeliveryId,
                    correlation.CorrelationId,
                    Data: new Dictionary<string, string>
                    {
                        ["kind"] = context.Kind,
                        ["resourceType"] = resourceType,
                        ["resourceId"] = context.ResourceId ?? string.Empty,
                    },
                    Link: context.Link,
                    SkipWhenNoDevices: true
                ),
                ct
            );
        }
    }

    private async Task SendEmailsAsync(OffboardingNoticeContext context, OffboardingNotice notice, CancellationToken ct)
    {
        // El gate de preferencias del dispatcher no cubre el correo: se filtra acá (lo obligatorio no se filtra).
        var skipPreference =
            NotificationCategoryRules.IsLocked(context.Category)
            || NotificationCatalog.GetRequired(context.Kind).IsMandatoryFor(NotificationChannel.Email);
        var addresses = new List<string>();
        foreach (var userId in notice.Recipients)
        {
            if (
                !skipPreference
                && !await preferences.IsEnabledAsync(
                    context.TenantId,
                    userId,
                    context.Category,
                    NotificationChannel.Email,
                    ct
                )
            )
                continue;
            var address = await emails.ResolveAsync(context.TenantId, userId, ct);
            if (!string.IsNullOrWhiteSpace(address) && !addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
                addresses.Add(address);
        }
        if (addresses.Count == 0)
            return;

        var render = (
            await scribe.RenderAsync(context.EmailEventKey, context.TenantId, notice.EmailVariables, ct)
        ).EnsureRendered(context.EmailEventKey);

        foreach (var address in addresses)
            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    context.TenantId,
                    address,
                    context.EmailEventKey,
                    context.DeliveryId,
                    correlation.CorrelationId
                ),
                ct
            );
    }

    private Task RecordInAppAsync(
        OffboardingNoticeContext context,
        OffboardingNotice notice,
        Guid userId,
        CancellationToken ct
    ) =>
        dispatcher.RecordInAppAsync(
            context.TenantId,
            $"user:{userId:N}",
            notice.Title,
            context.Category,
            context.Kind,
            context.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                context.Kind,
                userId,
                context.EventId,
                context.SourceEventType,
                context.OccurredOn,
                notice.Title,
                FallbackBody: notice.Body,
                ResourceId: context.ResourceId,
                ResourceContext: context.ResourceContext,
                Params: notice.Params,
                // Un aviso sin leer del mismo recurso se actualiza en vez de duplicarse.
                GroupKey: context.GroupKey
            ),
            ct: ct
        );
}
