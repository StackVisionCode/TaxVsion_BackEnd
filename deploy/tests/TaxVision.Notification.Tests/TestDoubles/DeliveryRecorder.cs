using System.Collections;
using System.Text.Json;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>
/// Caracterización de la entrega de un consumer: filas de log por canal, pushes, SMS, correos pedidos, renders y
/// eventos publicados, como líneas ordenadas con los ids cambiados por alias. Sirve igual antes y después de
/// pasar el consumer al punto único.
/// </summary>
internal sealed class DeliveryRecorder
{
    private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

    public DeliveryRecorder(string language = "en", Guid? portalUserId = null)
    {
        Locales = new FixedRecipientLocale(language);
        // Con cuenta de portal, el enlace de chat y llamadas de ese usuario va al portal.
        Links = TestPushLinks.Create(portalUserId);
        var gate = TestDeliveryGates.For(Preferences, Directory);
        Email = new RecordingGateway(
            new EventBasedEmailDispatchGateway(
                Publisher,
                Logs,
                new CountingUnitOfWork(),
                gate,
                NullLogger<EventBasedEmailDispatchGateway>.Instance
            )
        );
        Dispatcher = TestDispatchers.Create(
            Sms,
            PushSender,
            Devices,
            Logs,
            gate,
            Publisher,
            new CountingUnitOfWork(),
            new NoOpNotificationDispatchMetrics(),
            new InlinePushAttemptRecorder(Logs),
            Email,
            Scribe,
            Locales,
            Links
        );
    }

    public RecordingNotificationLogs Logs { get; } = new();
    public RecordingSms Sms { get; } = new();
    public CapturingIntegrationEventPublisher Publisher { get; } = new();
    public InMemoryPushDevices Devices { get; } = new();
    public RecordingPush PushSender { get; } = new();
    public OffPreferences Preferences { get; } = new();
    public InMemoryUserEmailDirectoryRepository Directory { get; } = new();
    public RecordingScribeRenderClient Scribe { get; } = new();
    public FixedRecipientLocale Locales { get; }
    public PushLinks Links { get; }
    public RecordingGateway Email { get; }
    public NotificationDispatcher Dispatcher { get; }
    public TestCorrelationContext Correlation { get; } = new();

    /// <summary>El id aparece como <paramref name="name"/> en las líneas.</summary>
    public DeliveryRecorder Alias(Guid id, string name)
    {
        aliases[id.ToString()] = name;
        aliases[id.ToString("N")] = name;
        return this;
    }

    /// <summary>Un dispositivo web para el usuario (token = alias del usuario).</summary>
    public DeliveryRecorder WithDevice(Guid tenantId, Guid userId)
    {
        Devices.All.Add(PushDeviceToken.Register(tenantId, userId, PushPlatform.Web, $"token-{userId}", null).Value);
        return this;
    }

    public string[] Lines()
    {
        var lines = new List<string>();
        lines.AddRange(
            Logs.Logs.Select(log =>
                $"log {log.Channel} kind={log.Kind} tpl={log.TemplateKey} to={log.Recipient} status={log.Status} event={log.RelatedEventId} subject={log.Subject}"
            )
        );
        lines.AddRange(
            PushSender.Sent.Select(push =>
                $"push token={push.Token} title={push.Title} body={push.Body} link={push.Link} tag={push.Tag} ttl={push.TimeToLive} data={Map(push.Data)}"
            )
        );
        lines.AddRange(
            Email.Requests.Select(email =>
                $"email to={email.To} tpl={email.TemplateKey} event={email.RelatedEventId} scope={email.Scope} confidential={email.Confidential} idem={email.IdempotencyKey} attachments={email.InlineAttachments?.Count ?? 0}"
            )
        );
        lines.AddRange(
            Sms.Sent.Select(sms =>
                $"sms to={sms.Phone} tpl={sms.TemplateKey} idem={sms.IdempotencyToken} text={sms.Text}"
            )
        );
        lines.AddRange(
            Scribe.Renders.Select(render =>
                $"render {render.EventKey} audience={render.Audience} vars={Map(render.Variables)}"
            )
        );
        lines.AddRange(Publisher.Published.Select(Event));
        return lines.Select(Replace).Order(StringComparer.Ordinal).ToArray();
    }

    private string Replace(string line)
    {
        foreach (var (id, name) in aliases)
            line = line.Replace(id, name, StringComparison.OrdinalIgnoreCase);
        return line;
    }

    private static string Event(object evt) =>
        evt is InAppNotificationRequestedIntegrationEvent inApp
            ? $"inapp kind={inApp.Kind} to={inApp.RecipientUserId} cat={inApp.Category} priority={inApp.Priority} "
                + $"title={inApp.FallbackTitle} body={inApp.FallbackBody} rt={inApp.ResourceType} rid={inApp.ResourceId} "
                + $"ctx={Map(inApp.ResourceContext)} params={Map(inApp.Params)} action={inApp.Action} "
                + $"src={inApp.SourceEventId} srcType={inApp.SourceEventType} group={inApp.GroupKey} "
                + $"actor={inApp.ActorUserId}/{inApp.ActorDisplayName} at={inApp.OccurredAtUtc:O}"
            : $"event {evt.GetType().Name} "
                + string.Join(
                    " ",
                    new[] { "To", "TemplateKey", "Kind", "RecipientUserId" }
                        .Select(name => (name, value: evt.GetType().GetProperty(name)?.GetValue(evt)))
                        .Where(pair => pair.value is not null)
                        .Select(pair => $"{pair.name}={Value(pair.value)}")
                );

    private static string Map<T>(IEnumerable<KeyValuePair<string, T>>? map) =>
        map is null
            ? "-"
            : string.Join(
                ";",
                map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Value(pair.Value)}")
            );

    private static string Value(object? value) =>
        value switch
        {
            null => "null",
            string text => text,
            Guid or DateTime or bool or int or long or decimal => Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture
            )!,
            IEnumerable and not IDictionary => JsonSerializer.Serialize(value),
            _ when value.GetType().IsPrimitive || value.GetType().IsEnum => value.ToString()!,
            _ => JsonSerializer.Serialize(value),
        };

    internal sealed class RecordingGateway(IEmailDispatchGateway inner) : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Requests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return inner.QueueEmailAsync(request, ct);
        }
    }

    internal sealed class RecordingPush : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>Preferencias encendidas salvo los pares apagados.</summary>
    internal sealed class OffPreferences : IUserNotificationPreferenceRepository
    {
        public HashSet<(NotificationCategory, NotificationChannel)> Off { get; } = [];

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(!Off.Contains((category, channel)));

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    internal sealed class RecordingSms : ISmsSender
    {
        public List<(string Phone, string Text, string? TemplateKey, string? IdempotencyToken)> Sent { get; } = [];

        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken,
            string? sourceRef = null
        )
        {
            Sent.Add((phoneNumber, text, templateKey, idempotencyToken));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class CountingUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
