using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Arma el dispatcher con sus tres canales sobre los dobles de cada prueba.</summary>
internal static class TestDispatchers
{
    public static NotificationDispatcher Create(
        ISmsSender smsSender,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logs,
        NotificationDeliveryGate deliveryGate,
        IIntegrationEventPublisher publisher,
        IUnitOfWork unitOfWork,
        INotificationDispatchMetrics metrics,
        IPushAttemptRecorder pushAttempts
    )
    {
        var gate = new DispatchChannelGate(deliveryGate, metrics);
        return new NotificationDispatcher(
            new SmsDispatch(smsSender, logs, gate, unitOfWork, metrics),
            new PushDispatch(
                pushSender,
                pushDeviceTokens,
                logs,
                gate,
                unitOfWork,
                metrics,
                pushAttempts,
                NullLogger<PushDispatch>.Instance
            ),
            new InAppDispatch(logs, gate, publisher, unitOfWork, metrics, NullLogger<InAppDispatch>.Instance)
        );
    }
}
