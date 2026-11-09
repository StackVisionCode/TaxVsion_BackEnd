using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Anota cada métrica de despacho como "resultado:kind:canal".</summary>
public sealed class RecordingDispatchMetrics : INotificationDispatchMetrics
{
    public List<string> Records { get; } = [];

    public void RecordClientFormPushFailure(string code) { }

    public void RecordSent(string kind, string channel) => Records.Add($"sent:{kind}:{channel}");

    public void RecordSuppressedByPreference(string kind, string channel) =>
        Records.Add($"suppressed:{kind}:{channel}");

    public void RecordFailed(string kind, string channel) => Records.Add($"failed:{kind}:{channel}");

    public void RecordNoRecipient(string kind, string channel) => Records.Add($"none:{kind}:{channel}");
}
