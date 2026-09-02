using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

// Fase 4 del plan de notificaciones dinámicas: estos 2 consumers son los únicos migrados al
// mecanismo de fan-out por permiso (NotificationAudience/IRecipientResolver) — antes usaban el
// placeholder de texto "role:TenantAdmin" como Recipient, que NotificationDispatcher solo
// persistía tal cual en NotificationLog sin resolver a ningún usuario real. El resto de los
// consumers de Notification ya tenían un destinatario explícito real y quedan sin tocar (ver
// plan, Fase 4, alcance explícito).
//
// Fase 8 (piloto de punta a punta): además del in-app, ahora también despachan push
// (SendPushAsync) por cada destinatario resuelto — el mismo IsAllowedAsync interno del
// dispatcher aplica la preferencia de canal Push de la Fase 5, y si el usuario no tiene ningún
// PushDeviceToken activo, SendPushAsync falla en silencio con Notification.NoPushDevices sin
// interrumpir el resto del fan-out (mismo criterio best-effort que el resto del dispatcher).
// Este par de consumers es el caso de uso elegido por el plan para demostrar las Fases 4+5+7
// funcionando juntas de punta a punta.

public static class FileInfectedDetectedConsumer
{
    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await recipientResolver.ResolveAsync(
                new ByPermission(evt.TenantId, CloudStoragePermissions.SettingsManage),
                ct
            );
            var title = $"Archivo malicioso bloqueado ({evt.FileId:N})";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    userId.ToString("N"),
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file_infected",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    ct: ct
                );
                await dispatcher.SendPushAsync(
                    evt.TenantId,
                    userId,
                    title,
                    "Revisá el panel de almacenamiento para más detalles.",
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.file_infected",
                    evt.EventId,
                    correlation.CorrelationId,
                    ct
                );
            }
        }
    }
}

/// <summary>
/// Emaila la página pública branded de un share-link a un destinatario externo (visibility
/// ExternalRecipients). Un evento por destinatario; el link lleva el <c>?email</c> que el backend
/// valida. Mismo patrón que SignerInvitedConsumer: render en Scribe + despacho por el gateway,
/// idempotente por RelatedEventId (EventId único por destinatario).
/// </summary>
public static class ShareLinkExternalRecipientInvitedConsumer
{
    private const string TemplateKey = "storage.share_invited.v1";

    public static async Task Handle(
        ShareLinkExternalRecipientInvitedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        ILogger<ShareLinkExternalRecipientInvitedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            // El destinatario abre la página branded en el subdominio de la oficina.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var openLink = TenantEmailLinks.PublicSharePageLink(tenantHost, portal.Value, evt.PlainToken, evt.Email);

            var render = (
                await scribeClient.RenderAsync(
                    TemplateKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["file_name"] = evt.FileName,
                        ["open_link"] = openLink,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["permission"] = evt.Permission,
                        ["language"] = evt.Language,
                    },
                    ct
                )
            ).EnsureRendered(TemplateKey);

            var result = await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: TemplateKey,
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );

            if (!result.IsSuccess)
                logger.LogWarning(
                    "ShareLinkExternalRecipientInvited email failed for share {ShareLinkId}: {Error}",
                    evt.ShareLinkId,
                    result.Error
                );
        }
    }
}

public static class StorageLimitExceededConsumer
{
    public static async Task Handle(
        StorageLimitExceededIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await recipientResolver.ResolveAsync(
                new ByPermission(evt.TenantId, CloudStoragePermissions.SettingsManage),
                ct
            );
            const string title = "Límite de almacenamiento alcanzado";
            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    userId.ToString("N"),
                    title,
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.quota_exceeded",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    ct: ct
                );
                await dispatcher.SendPushAsync(
                    evt.TenantId,
                    userId,
                    title,
                    "El tenant alcanzó su cuota de almacenamiento contratada.",
                    NotificationCategory.StorageAndQuota,
                    "cloudstorage.quota_exceeded",
                    evt.EventId,
                    correlation.CorrelationId,
                    ct
                );
            }
        }
    }
}
