using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
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
    IScribeRenderClient scribe,
    ITenantHostResolver hosts,
    IOptions<PortalOptions> portal,
    ICorrelationContext correlation,
    IRecipientLocale locales
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

        foreach (var notice in notices)
        foreach (var userId in notice.Recipients)
        {
            await RecordInAppAsync(context, notice, userId, ct);
            // Conserva su enlace directo (lleva parámetros que /n/open no transporta).
            await dispatcher.DispatchAsync(
                Intent(context, new NotificationTarget(userId), context.DeliveryId) with
                {
                    Push = new PushContent(
                        ResourceId: context.ResourceId,
                        SourceEventId: context.EventId,
                        SkipWhenNoDevices: true,
                        Title: notice.Title,
                        Body: notice.Body,
                        Link: context.Link
                    ),
                },
                ct
            );
        }
    }

    private async Task SendEmailsAsync(OffboardingNoticeContext context, OffboardingNotice notice, CancellationToken ct)
    {
        // Un correo por dirección, con su usuario: el punto único aplica inactivo, preferencia y catálogo.
        var addresses = new List<(Guid UserId, string Address)>();
        foreach (var userId in notice.Recipients)
        {
            var address = await emails.ResolveAsync(context.TenantId, userId, ct);
            if (
                !string.IsNullOrWhiteSpace(address)
                && !addresses.Any(known => string.Equals(known.Address, address, StringComparison.OrdinalIgnoreCase))
            )
                addresses.Add((userId, address));
        }
        if (addresses.Count == 0)
            return;

        // Un render por idioma y lector; todos antes de encolar.
        var renders = new RecipientRenders(
            scribe,
            context.EmailEventKey,
            context.TenantId,
            ScribeRenderAudience.Staff,
            notice.EmailVariables
        );
        var languages = new RecipientLocaleCache(locales, context.TenantId);
        var targets = new List<NotificationTarget>(addresses.Count);
        foreach (var (userId, address) in addresses)
        {
            var locale = await languages.ForUserAsync(userId, ct);
            await renders.ForAsync(locale, ct);
            targets.Add(new NotificationTarget(userId, address, Locale: locale));
        }

        foreach (var target in targets)
            await dispatcher.DispatchAsync(
                Intent(context, target, context.DeliveryId) with
                {
                    Email = new EmailContent(context.EmailEventKey, notice.EmailVariables, Render: renders.ForAsync),
                },
                ct
            );
    }

    private Task RecordInAppAsync(
        OffboardingNoticeContext context,
        OffboardingNotice notice,
        Guid userId,
        CancellationToken ct
    ) =>
        dispatcher.DispatchAsync(
            Intent(context, new NotificationTarget(userId), context.EventId) with
            {
                InApp = new InAppContent(
                    Params: notice.Params,
                    ResourceId: context.ResourceId,
                    ResourceContext: context.ResourceContext,
                    // Un aviso sin leer del mismo recurso se actualiza en vez de duplicarse.
                    GroupKey: context.GroupKey,
                    FallbackTitle: notice.Title,
                    FallbackBody: notice.Body
                ),
            },
            ct
        );

    // Correo y push van por DeliveryId (dedupe del hecho); la campana, por el evento.
    private NotificationIntent Intent(
        OffboardingNoticeContext context,
        NotificationTarget target,
        Guid sourceEventId
    ) =>
        new(
            context.Kind,
            context.TenantId,
            target,
            sourceEventId,
            context.SourceEventType,
            context.OccurredOn,
            correlation.CorrelationId
        );
}
