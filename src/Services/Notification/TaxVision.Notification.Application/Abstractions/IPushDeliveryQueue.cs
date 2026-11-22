using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Cola local del envío a FCM (N8-04). Lo encolado entra en el outbox del handler que lo pide.</summary>
public interface IPushDeliveryQueue
{
    Task EnqueueAsync(DeliverPush command, CancellationToken ct = default);
}
