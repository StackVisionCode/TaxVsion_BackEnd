using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Dobles de los consumers de reunión (F6): gateway, render, logs, preferencia y correlación.</summary>
internal sealed class RecordingEmailGateway : IEmailDispatchGateway
{
    public List<EmailDispatchRequest> Queued { get; } = [];

    public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
    {
        Queued.Add(request);
        return Task.FromResult(
            new EmailDispatchResult(Guid.NewGuid(), Guid.NewGuid(), NotificationDispatchAttemptStatus.Sent, null, null)
        );
    }
}

internal sealed class RecordingScribeRenderClient : IScribeRenderClient
{
    public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } = new Dictionary<string, object?>();
    public string? LastEventKey { get; private set; }
    public int Calls { get; private set; }

    public Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    )
    {
        Calls++;
        LastEventKey = eventKey;
        LastVariables = variables;
        return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>html</p>", "text")));
    }
}

internal sealed class RecordingNotificationLogs : INotificationLogRepository
{
    public List<NotificationLog> Logs { get; } = [];

    public Task AddAsync(NotificationLog log, CancellationToken ct = default)
    {
        Logs.Add(log);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        NotificationStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((Logs, Logs.Count));

    public Task<NotificationLog?> GetByRelatedEventIdAsync(
        Guid tenantId,
        Guid relatedEventId,
        string templateKey,
        string recipient,
        CancellationToken ct = default
    ) => Task.FromResult<NotificationLog?>(null);
}

internal sealed class SwitchableNotificationPreferences(bool enabled) : IUserNotificationPreferenceRepository
{
    public NotificationCategory? LastAskedCategory { get; private set; }
    public NotificationChannel? LastAskedChannel { get; private set; }

    public Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct = default
    )
    {
        LastAskedCategory = category;
        LastAskedChannel = channel;
        return Task.FromResult(enabled);
    }

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

    public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class TestCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test";

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId) => new NoOpScope();

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Decodifica el adjunto en memoria para leer el .ics en los asserts.</summary>
internal static class InlineAttachmentReader
{
    public static string Text(BuildingBlocks.Messaging.EmailIntegrationEvents.EmailInlineAttachment attachment) =>
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(attachment.ContentBase64));
}
