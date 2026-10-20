using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>No-op para tests que construyen `NotificationDispatcher` directamente —
/// los contadores de Fase 11 no son parte del contrato que estos tests validan.</summary>
public sealed class NoOpNotificationDispatchMetrics : INotificationDispatchMetrics
{
    public void RecordSent(string kind, string channel) { }

    public void RecordSuppressedByPreference(string kind, string channel) { }

    public void RecordFailed(string kind, string channel) { }

    public void RecordNoRecipient(string kind, string channel) { }
}
