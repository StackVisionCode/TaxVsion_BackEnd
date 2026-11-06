using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Delivery;

/// <summary>
/// Consume <c>postmaster.correspondence_delivery_feedback.v1</c>: el resultado de entrega de un correo enviado desde
/// el CRM, ya correlacionado por Postmaster (Message-ID + destinatario). Actualiza el estado del destinatario en el
/// enviado y, si no se entregó o se demoró, deja el aviso en su conversación (creándola si el correo era nuevo).
/// <para>
/// Es la ÚNICA puerta de un aviso de no entrega al Inbox: <c>RawMessageReceivedConsumer</c> sigue descartando
/// los remitentes que no son clientes (mailer-daemon incluido), así que no hay forma de inundar el Inbox.
/// </para>
/// <para>
/// Idempotente: Postmaster publica uno por cambio de resultado con EventId aleatorio, así que la clave es
/// (envío, destinatario, resultado). Guarda antes de publicar.
/// </para>
/// </summary>
public static class PostmasterCorrespondenceDeliveryFeedbackConsumer
{
    public static async Task Handle(
        PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent evt,
        IDraftRepository drafts,
        IEmailThreadRepository threads,
        IDeliveryNoticeRepository notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<DeliveryNotice> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            if (!TryParseOutcome(evt.Outcome, out var outcome))
            {
                logger.LogWarning(
                    "Delivery feedback {EventId} has an unknown outcome {Outcome}; skipping.",
                    evt.EventId,
                    evt.Outcome
                );
                return;
            }

            if (
                outcome != DeliveryOutcome.Delivered
                && await notices.ExistsAsync(evt.TenantId, evt.SentMessageId, evt.Recipient, outcome, ct)
            )
            {
                logger.LogDebug("Delivery notice already recorded for {SentMessageId}; skipping.", evt.SentMessageId);
                return;
            }

            var draft = await drafts.GetByIdAsync(evt.TenantId, evt.CorrespondenceDraftId, ct);
            // Postmaster puede avisar antes de que el borrador quede Sent: se reintenta con espera larga y después DLQ.
            if (draft is null || draft.Status is DraftStatus.Draft or DraftStatus.Sending)
                throw new DependencyNotReadyException("CorrespondenceDraft", $"draft {evt.CorrespondenceDraftId}");

            if (draft.Status != DraftStatus.Sent)
            {
                logger.LogWarning(
                    "Delivery feedback {EventId} points to draft {DraftId} in terminal status {Status}; skipping.",
                    evt.EventId,
                    evt.CorrespondenceDraftId,
                    draft.Status
                );
                return;
            }

            var recorded = draft.RecordDeliveryOutcome(evt.Recipient, outcome, evt.Category, evt.EventAtUtc);
            if (recorded.IsFailure)
                logger.LogWarning(
                    "Delivery feedback {EventId}: {Error}; the notice is kept without updating the recipient status.",
                    evt.EventId,
                    recorded.Error.Code
                );

            if (outcome == DeliveryOutcome.Delivered)
            {
                if (recorded is { IsSuccess: true, Value: true })
                    await unitOfWork.SaveChangesAsync(ct);
                return;
            }

            var threadResult = await ResolveThreadAsync(draft, threads, ct);
            if (threadResult.IsFailure)
            {
                logger.LogWarning(
                    "Delivery feedback {EventId}: no conversation for draft {DraftId} ({Error}).",
                    evt.EventId,
                    draft.Id,
                    threadResult.Error.Code
                );
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }
            var thread = threadResult.Value;

            var noticeResult = DeliveryNotice.Create(
                evt.TenantId,
                thread.Id,
                draft.Id,
                evt.SentMessageId,
                draft.CustomerId,
                draft.AccountId,
                evt.Recipient,
                outcome,
                evt.Category,
                evt.StatusCode,
                evt.DiagnosticSnippet,
                evt.Suppressed,
                evt.ReportAccountId,
                evt.ReportProviderMessageId,
                evt.EventAtUtc,
                DateTime.UtcNow
            );
            if (noticeResult.IsFailure)
            {
                logger.LogWarning(
                    "Delivery feedback {EventId}: notice rejected ({Error}).",
                    evt.EventId,
                    noticeResult.Error.Code
                );
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }
            var notice = noticeResult.Value;

            await notices.AddAsync(notice, ct);
            thread.RegisterDeliveryNotice(notice.ReceivedAtUtc, notice.RequiresAction);
            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new CorrespondenceDeliveryNoticeReceivedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    CorrelationId = evt.CorrelationId,
                    DeliveryNoticeId = notice.Id,
                    EmailThreadId = thread.Id,
                    DraftId = draft.Id,
                    CustomerId = draft.CustomerId,
                    MailboxAccountId = draft.AccountId,
                    SentByUserId = draft.CreatedByUserId,
                    Recipient = notice.Recipient,
                    Outcome = evt.Outcome,
                    Category = notice.Category,
                }
            );
            thread.NotifyStateChanged(correlationId: evt.CorrelationId);
        }
    }

    /// <summary>
    /// La conversación del enviado. Un correo nuevo no tiene: se crea con el id del propio enviado (dos avisos
    /// simultáneos chocan en la clave y el reintento ya la encuentra) y se enlaza.
    /// </summary>
    private static async Task<Result<EmailThread>> ResolveThreadAsync(
        Draft draft,
        IEmailThreadRepository threads,
        CancellationToken ct
    )
    {
        if (draft.EmailThreadId is { } threadId)
        {
            var existing = await threads.GetByIdAsync(draft.TenantId, threadId, ct);
            return existing is null
                ? Result.Failure<EmailThread>(new Error("EmailThread.NotFound", "The conversation no longer exists."))
                : Result.Success(existing);
        }

        var created = EmailThread.NewForSentEmail(
            draft.Id,
            draft.TenantId,
            draft.CustomerId,
            draft.Subject,
            draft.UpdatedAtUtc
        );
        if (created.IsFailure)
            return created;

        var link = draft.LinkToThread(created.Value.Id);
        if (link.IsFailure)
            return Result.Failure<EmailThread>(link.Error);

        await threads.AddAsync(created.Value, ct);
        return created;
    }

    private static bool TryParseOutcome(string? raw, out DeliveryOutcome outcome)
    {
        outcome = raw switch
        {
            CorrespondenceDeliveryOutcomes.Delivered => DeliveryOutcome.Delivered,
            CorrespondenceDeliveryOutcomes.Delayed => DeliveryOutcome.Delayed,
            CorrespondenceDeliveryOutcomes.NotDelivered => DeliveryOutcome.NotDelivered,
            _ => (DeliveryOutcome)(-1),
        };
        return Enum.IsDefined(outcome);
    }
}
