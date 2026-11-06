using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.DiscardUnsentMessage;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;
using TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;
using TaxVision.Postmaster.Application.Suppression;
using Wolverine;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// El envío completo como lo corre Wolverine: el paso transaccional del consumer y, si dejó un
/// <see cref="DispatchSentMessageCommand"/>, el paso de envío con los mismos dobles. Los comandos locales
/// (<c>InvokeAsync</c>) corren en línea; si uno lanza, se revierten las reservas del guard falso.
/// </summary>
internal static class EmailSendPipeline
{
    public static async Task RunAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        IIdempotencyGuard idempotencyGuard,
        IProviderResolver providerResolver,
        IConnectedMailboxResolver mailboxResolver,
        ISuppressionListRepository suppressionList,
        IEmailProviderRateLimiter rateLimiter,
        IEmailSender emailSender,
        IConnectedMailboxSender mailboxSender,
        IInlineAssetFetcher inlineAssetFetcher,
        IOutboundAttachmentFetcher attachmentFetcher,
        ISentMessageRepository sentMessages,
        ITenantDirectoryRepository tenantDirectory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        FakeMessageBus bus,
        ILogger logger,
        CancellationToken ct,
        Envelope? envelope = null
    )
    {
        RouteSendCommands(bus, idempotencyGuard, sentMessages, unitOfWork, mailboxResolver, suppressionList);
        await InTransactionAsync(
            idempotencyGuard,
            async () =>
            {
                await NotificationsEmailSendRequestedConsumer.Handle(
                    evt,
                    idempotencyGuard,
                    providerResolver,
                    mailboxResolver,
                    suppressionList,
                    rateLimiter,
                    sentMessages,
                    tenantDirectory,
                    unitOfWork,
                    correlation,
                    bus,
                    logger,
                    ct,
                    envelope
                );
                await DomainEventReplay.DispatchIntoAsync(bus, sentMessages);
                return null;
            }
        );

        var dispatch = bus.Published.OfType<DispatchSentMessageCommand>().SingleOrDefault();
        if (dispatch is null)
            return;
        bus.Published.Remove(dispatch);
        await DispatchAsync(
            dispatch,
            sentMessages,
            providerResolver,
            rateLimiter,
            emailSender,
            mailboxSender,
            inlineAssetFetcher,
            attachmentFetcher,
            tenantDirectory,
            correlation,
            bus,
            logger,
            ct,
            envelope
        );
    }

    /// <summary>El paso de envío solo (p. ej. una nueva entrega del comando durable).</summary>
    public static Task DispatchAsync(
        DispatchSentMessageCommand dispatch,
        ISentMessageRepository sentMessages,
        IProviderResolver providerResolver,
        IEmailProviderRateLimiter rateLimiter,
        IEmailSender emailSender,
        IConnectedMailboxSender mailboxSender,
        IInlineAssetFetcher inlineAssetFetcher,
        IOutboundAttachmentFetcher attachmentFetcher,
        ITenantDirectoryRepository tenantDirectory,
        ICorrelationContext correlation,
        FakeMessageBus bus,
        ILogger logger,
        CancellationToken ct,
        Envelope? envelope = null
    ) =>
        DispatchSentMessageHandler.Handle(
            dispatch,
            sentMessages,
            providerResolver,
            rateLimiter,
            emailSender,
            mailboxSender,
            inlineAssetFetcher,
            attachmentFetcher,
            tenantDirectory,
            correlation,
            bus,
            logger,
            ct,
            envelope
        );

    /// <summary>Cada comando local corre como su propio handler transaccional de Wolverine.</summary>
    public static void RouteSendCommands(
        FakeMessageBus bus,
        IIdempotencyGuard idempotencyGuard,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        IConnectedMailboxResolver mailboxResolver,
        ISuppressionListRepository suppressionList
    ) =>
        bus.OnInvoke = message =>
            InTransactionAsync(
                idempotencyGuard,
                async () =>
                    message switch
                    {
                        MarkSendAttemptedCommand mark => await MarkSendAttemptedHandler.Handle(
                            mark,
                            sentMessages,
                            unitOfWork,
                            CancellationToken.None
                        ),
                        ReleaseSendAttemptCommand release => await Run(
                            ReleaseSendAttemptHandler.Handle(release, sentMessages, unitOfWork, CancellationToken.None)
                        ),
                        RecordSendResultCommand record => await RecordAsync(
                            record,
                            sentMessages,
                            idempotencyGuard,
                            unitOfWork,
                            bus
                        ),
                        DiscardUnsentMessageCommand discard => await Run(
                            DiscardUnsentMessageHandler.Handle(
                                discard,
                                sentMessages,
                                idempotencyGuard,
                                unitOfWork,
                                CancellationToken.None
                            )
                        ),
                        PrepareCorrespondenceMessageCommand prepare => await PrepareCorrespondenceMessageHandler.Handle(
                            prepare,
                            mailboxResolver,
                            suppressionList,
                            sentMessages,
                            idempotencyGuard,
                            unitOfWork,
                            CancellationToken.None
                        ),
                        _ => throw new NotSupportedException(message.GetType().Name),
                    }
            );

    private static async Task<object?> RecordAsync(
        RecordSendResultCommand record,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        FakeMessageBus bus
    )
    {
        await RecordSendResultHandler.Handle(
            record,
            sentMessages,
            idempotencyGuard,
            unitOfWork,
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, sentMessages);
        return null;
    }

    private static async Task<object?> Run(Task task)
    {
        await task;
        return null;
    }

    private static async Task<object?> InTransactionAsync(IIdempotencyGuard guard, Func<Task<object?>> handler)
    {
        var fake = guard as FakeIdempotencyGuard;
        var snapshot = fake?.Snapshot();
        try
        {
            return await handler();
        }
        catch when (fake is not null)
        {
            fake.Restore(snapshot!);
            throw;
        }
    }
}
