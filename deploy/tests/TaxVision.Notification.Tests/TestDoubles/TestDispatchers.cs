using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Arma el dispatcher con sus canales sobre los dobles de cada prueba.</summary>
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
        IPushAttemptRecorder pushAttempts,
        IEmailDispatchGateway? emailGateway = null,
        IScribeRenderClient? scribe = null,
        IRecipientLocale? locales = null,
        PushLinks? links = null,
        IUserEmailDirectoryRepository? directory = null,
        IPushDeliveryQueue? pushQueue = null,
        TimeProvider? clock = null,
        ICustomerPortalAccountProjectionRepository? portalAccounts = null
    )
    {
        var gate = new DispatchChannelGate(deliveryGate, metrics);
        var sms = new SmsDispatch(smsSender, logs, gate, unitOfWork, metrics);
        var push = new PushDispatch(
            pushSender,
            pushDeviceTokens,
            logs,
            gate,
            unitOfWork,
            metrics,
            pushAttempts,
            clock ?? TimeProvider.System,
            NullLogger<PushDispatch>.Instance
        );
        // Sin cola propia, el push encolado se entrega en el acto.
        pushQueue ??= new InlinePushDeliveryQueue(push);
        var inApp = new InAppDispatch(
            logs,
            gate,
            publisher,
            unitOfWork,
            metrics,
            directory ?? new InMemoryUserEmailDirectoryRepository(),
            portalAccounts ?? new TestPortalAccounts(),
            NullLogger<InAppDispatch>.Instance
        );
        return new NotificationDispatcher(
            sms,
            push,
            inApp,
            pushQueue,
            new NotificationIntentDispatch(
                sms,
                push,
                pushQueue,
                inApp,
                emailGateway ?? new UnusedEmailGateway(),
                scribe ?? new UnusedScribe(),
                locales ?? FixedRecipientLocale.English,
                links ?? TestPushLinks.Create(),
                deliveryGate,
                metrics,
                NullLogger<NotificationIntentDispatch>.Instance
            )
        );
    }

    // Las pruebas que no despachan correo por la intención no los tocan.
    internal sealed class UnusedEmailGateway : IEmailDispatchGateway
    {
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) => throw new NotSupportedException("Pass an email gateway to the test dispatcher.");
    }

    internal sealed class UnusedScribe : IScribeRenderClient
    {
        public Task<BuildingBlocks.Results.Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => throw new NotSupportedException("Pass a Scribe client to the test dispatcher.");

        public Task<BuildingBlocks.Results.Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => throw new NotSupportedException("Pass a Scribe client to the test dispatcher.");

        public Task<BuildingBlocks.Results.Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => ScribeTestDefaults.TextUnsupported();
    }
}
