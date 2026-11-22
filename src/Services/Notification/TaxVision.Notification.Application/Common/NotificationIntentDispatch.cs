using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Ejecuta una <see cref="NotificationIntent"/>: el catálogo decide canales, categoría y obligatoriedad; el gate
/// de siempre decide por destinatario; cada canal se delega en su pieza (in-app, push, SMS y el gateway de correo).
/// </summary>
public sealed class NotificationIntentDispatch(
    SmsDispatch smsDispatch,
    PushDispatch pushDispatch,
    IPushDeliveryQueue pushQueue,
    InAppDispatch inAppDispatch,
    IEmailDispatchGateway emailGateway,
    IScribeRenderClient scribe,
    IRecipientLocale locales,
    PushLinks links,
    NotificationDeliveryGate deliveryGate,
    INotificationDispatchMetrics metrics,
    ILogger<NotificationIntentDispatch> logger
)
{
    public async Task<DispatchSummary> DispatchAsync(NotificationIntent intent, CancellationToken ct = default)
    {
        NotificationCatalog.TryGet(intent.Kind, out var entry);
        var plan = NotificationDispatchPlanner.Plan(intent, entry);
        var outcomes = new Dictionary<NotificationChannel, ChannelOutcome>();
        var errors = new Dictionary<NotificationChannel, Error>();
        RecipientLocale? locale = null;

        foreach (var channel in plan.Channels)
        {
            if (channel.SkippedAs is { } skipped)
            {
                outcomes[channel.Channel] = skipped;
                RecordSkip(intent.Kind, channel.Channel, skipped);
                continue;
            }

            var category = plan.PreferenceCategory!.Value;
            var outcome = await GateAsync(intent, channel.Channel, category, ct);
            if (outcome is null)
            {
                // El in-app no usa idioma: el frontend traduce por kind.
                if (channel.Channel != NotificationChannel.InApp)
                    locale ??= await LocaleAsync(intent.TenantId, intent.Target, ct);
                outcome = channel.Channel switch
                {
                    NotificationChannel.InApp => await InAppAsync(intent, category, ct),
                    NotificationChannel.Push => await PushAsync(intent, category, locale!, errors, ct),
                    NotificationChannel.Email => await EmailAsync(intent, category, locale!, ct),
                    _ => await SmsAsync(intent, category, locale!, ct),
                };
            }
            outcomes[channel.Channel] = outcome.Value;
        }

        return new DispatchSummary(outcomes, errors.Count == 0 ? null : errors);
    }

    private void RecordSkip(string kind, NotificationChannel channel, ChannelOutcome skipped)
    {
        if (skipped == ChannelOutcome.Failed)
        {
            logger.LogError(
                "Notification kind {Kind} is not in the catalog; nothing sent on {Channel}.",
                kind,
                channel
            );
            metrics.RecordFailed(kind, channel.ToString());
        }
        else if (skipped == ChannelOutcome.NotAllowedByCatalog)
        {
            logger.LogWarning(
                "Channel {Channel} is not allowed by the catalog for kind {Kind}; not sent.",
                channel,
                kind
            );
            metrics.RecordNotAllowedByCatalog(kind, channel.ToString());
        }
    }

    // Destino, destinatario inactivo y preferencia (lo obligatorio por catálogo pasa). Null = enviar.
    private async Task<ChannelOutcome?> GateAsync(
        NotificationIntent intent,
        NotificationChannel channel,
        NotificationCategory category,
        CancellationToken ct
    )
    {
        var target = intent.Target;
        var hasDestination = channel switch
        {
            // Sin usuario, el in-app de un invitado con correo deja solo su fila de auditoría.
            NotificationChannel.InApp => HasUser(target) || !string.IsNullOrWhiteSpace(target.Email),
            NotificationChannel.Push => HasUser(target),
            NotificationChannel.Email => !string.IsNullOrWhiteSpace(target.Email),
            _ => !string.IsNullOrWhiteSpace(target.Phone),
        };
        if (!hasDestination)
        {
            metrics.RecordNoRecipient(intent.Kind, channel.ToString());
            return ChannelOutcome.NoDestination;
        }
        if (await deliveryGate.IsRecipientInactiveAsync(intent.TenantId, target.UserId, intent.Kind, channel, ct))
        {
            metrics.RecordNoRecipient(intent.Kind, channel.ToString());
            return ChannelOutcome.RecipientInactive;
        }
        if (
            !await deliveryGate.IsAllowedByPreferenceAsync(
                intent.TenantId,
                target.UserId,
                category,
                channel,
                intent.Kind,
                ct
            )
        )
        {
            metrics.RecordSuppressedByPreference(intent.Kind, channel.ToString());
            return ChannelOutcome.SuppressedByPreference;
        }
        return null;
    }

    private async Task<RecipientLocale> LocaleAsync(Guid tenantId, NotificationTarget target, CancellationToken ct) =>
        target.Locale is { } known ? known
        : target.UserId is { } userId && userId != Guid.Empty ? await locales.ForUserAsync(tenantId, userId, ct)
        : target.CustomerId is { } customerId ? await locales.ForCustomerAsync(tenantId, customerId, ct)
        : RecipientLocale.English;

    private async Task<ChannelOutcome> InAppAsync(
        NotificationIntent intent,
        NotificationCategory category,
        CancellationToken ct
    )
    {
        var content = intent.InApp!;
        if (!HasUser(intent.Target))
            return await GuestInAppAsync(intent, category, content, ct);
        var userId = intent.Target.UserId!.Value;
        var (title, body) = content.FallbackTitle is { } ownTitle
            ? (ownTitle, content.FallbackBody)
            : InAppFallback.For(intent.Kind, content.Params);
        await inAppDispatch.RecordAsync(
            intent.TenantId,
            NotificationRecipient.ForUser(userId),
            title,
            category,
            content.TemplateKey ?? intent.Kind,
            intent.SourceEventId,
            intent.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                intent.Kind,
                userId,
                intent.SourceEventId,
                intent.SourceEventType,
                intent.OccurredAtUtc,
                title,
                FallbackBody: body,
                ActorUserId: content.ActorUserId,
                ActorDisplayName: content.ActorDisplayName,
                ResourceId: content.ResourceId,
                ResourceContext: content.ResourceContext,
                Params: content.Params,
                GroupKey: content.GroupKey
            ),
            ct
        );
        return ChannelOutcome.Sent;
    }

    private static bool HasUser(NotificationTarget target) => target.UserId is { } id && id != Guid.Empty;

    // Invitado sin cuenta: no hay campana, solo la fila de auditoría a su nombre (como antes del punto único).
    private async Task<ChannelOutcome> GuestInAppAsync(
        NotificationIntent intent,
        NotificationCategory category,
        InAppContent content,
        CancellationToken ct
    )
    {
        var (title, _) = content.FallbackTitle is { } ownTitle
            ? (ownTitle, content.FallbackBody)
            : InAppFallback.For(intent.Kind, content.Params);
        await inAppDispatch.RecordAsync(
            intent.TenantId,
            NotificationRecipient.ForGuest(intent.Target.Email!),
            title,
            category,
            content.TemplateKey ?? intent.Kind,
            intent.SourceEventId,
            intent.CorrelationId,
            ct: ct
        );
        return ChannelOutcome.Sent;
    }

    private async Task<ChannelOutcome> PushAsync(
        NotificationIntent intent,
        NotificationCategory category,
        RecipientLocale locale,
        Dictionary<NotificationChannel, Error> errors,
        CancellationToken ct
    )
    {
        var content = intent.Push!;
        var userId = intent.Target.UserId!.Value;
        var (title, body) = content.Title is { } ownTitle
            ? (ownTitle, content.Body ?? string.Empty)
            : PushText.For(content.TextKey ?? intent.Kind, locale);
        var baseUrl =
            content.WithoutLink || content.Link is not null ? null
            : content.LinkForRecipient ? await links.BaseForUserAsync(intent.TenantId, userId, ct)
            : content.ForClient ? await links.ClientBaseAsync(intent.TenantId, ct)
            : await links.StaffBaseAsync(intent.TenantId, ct);
        // El clic busca la fila de la bandeja por evento solo si este aviso la crea.
        var target = new PushTarget(
            intent.Kind,
            content.ResourceId ?? intent.InApp?.ResourceId,
            content.CustomerId ?? intent.Target.CustomerId,
            content.SourceEventId ?? (intent.InApp is null ? null : intent.SourceEventId)
        );
        var request = PushPayloads.Request(
            intent.TenantId,
            userId,
            title,
            body,
            category,
            target,
            intent.SourceEventId,
            intent.CorrelationId,
            baseUrl,
            content.Data,
            content.Tag,
            link: content.Link,
            skipWhenNoDevices: content.SkipWhenNoDevices,
            timeToLive: content.TimeToLive
        );
        // N8-04: FCM se llama desde la cola de push, salvo lo que tiene que salir en el handler.
        var outcome = content.Inline
            ? await pushDispatch.DispatchAsync(request, ct)
            : await pushDispatch.EnqueueAsync(request, pushQueue, ct);
        if (outcome.Error is { } error)
            errors[NotificationChannel.Push] = error;
        return outcome.Error switch
        {
            null => content.Inline ? ChannelOutcome.Sent : ChannelOutcome.Queued,
            { Code: "Notification.NoPushDevices" } => ChannelOutcome.NoDestination,
            _ => ChannelOutcome.Failed,
        };
    }

    private async Task<ChannelOutcome> EmailAsync(
        NotificationIntent intent,
        NotificationCategory category,
        RecipientLocale locale,
        CancellationToken ct
    )
    {
        var content = intent.Email!;
        var render = content.Render is { } own
            ? await own(locale, ct)
            : (
                await scribe.RenderForRecipientAsync(
                    content.EventKey,
                    intent.TenantId,
                    content.Variables,
                    content.Audience,
                    locale,
                    ct
                )
            ).EnsureRendered(content.EventKey);
        // Con usuario, el gateway vuelve a aplicar su gate: el correo del staff nunca lo salta.
        var userId = intent.Target.UserId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var result = await emailGateway.QueueEmailAsync(
            render.ToDispatchRequest(
                intent.TenantId,
                intent.Target.Email!,
                content.TemplateKey ?? content.EventKey,
                intent.SourceEventId,
                intent.CorrelationId
            ) with
            {
                RecipientUserId = userId,
                Category = userId is null ? null : category,
                NotificationKind = intent.Kind,
                Confidential = content.Confidential,
                IdempotencyKey = content.IdempotencyKey,
                InlineAttachments = content.InlineAttachments,
                AttachmentFileIds = content.AttachmentFileIds,
            },
            ct
        );
        return result.Status switch
        {
            NotificationDispatchAttemptStatus.Suppressed => ChannelOutcome.SuppressedByPreference,
            NotificationDispatchAttemptStatus.Failed => ChannelOutcome.Failed,
            _ => ChannelOutcome.Queued,
        };
    }

    private async Task<ChannelOutcome> SmsAsync(
        NotificationIntent intent,
        NotificationCategory category,
        RecipientLocale locale,
        CancellationToken ct
    )
    {
        var content = intent.Sms!;
        var text = SmsText.For(content.TextKey, locale, content.Params ?? new Dictionary<string, string>());
        await smsDispatch.SendAsync(
            intent.TenantId,
            intent.Target.Phone!,
            text,
            category,
            content.TemplateKey,
            intent.SourceEventId,
            intent.CorrelationId,
            intent.Target.UserId,
            content.IdempotencyToken,
            ct
        );
        return ChannelOutcome.Sent;
    }
}
