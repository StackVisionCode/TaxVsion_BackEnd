using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Entrega en el acto: las pruebas de consumers ven el push como si la cola ya hubiera corrido.</summary>
public sealed class InlinePushDeliveryQueue(PushDispatch push) : IPushDeliveryQueue
{
    public List<PushDispatchOutcome> Outcomes { get; } = [];

    public async Task EnqueueAsync(DeliverPush command, CancellationToken ct = default) =>
        Outcomes.Add(await push.DispatchAsync(command.Request, ct));
}

/// <summary>Solo guarda lo encolado.</summary>
public sealed class RecordingPushDeliveryQueue : IPushDeliveryQueue
{
    public List<DeliverPush> Queued { get; } = [];

    public Task EnqueueAsync(DeliverPush command, CancellationToken ct = default)
    {
        Queued.Add(command);
        return Task.CompletedTask;
    }
}

/// <summary>Evento de chat → carril masivo → comando, como lo encadena Wolverine.</summary>
internal static class ChatPushLane
{
    public static async Task RunAsync(
        ChatMessageSentIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        CancellationToken ct = default
    )
    {
        if (MobileCommunicationPushConsumer.Handle(evt) is { } command)
            await MobileCommunicationPushConsumer.Handle(command, dispatcher, ct);
    }
}
