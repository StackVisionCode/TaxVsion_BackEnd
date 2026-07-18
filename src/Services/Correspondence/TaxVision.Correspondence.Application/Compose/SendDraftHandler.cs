using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Audit;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// <c>POST /correspondence/drafts/{id}/send</c> (Fase 14, plan §0/§14/§36) — el cierre de la cadena
/// completa Correspondence → Postmaster → Connectors → proveedor real, como UNA llamada HTTP
/// síncrona y bloqueante: el usuario que aprieta "Enviar" espera el resultado real (éxito o falla)
/// en la MISMA request, igual que enviar desde Gmail. No hay evento, cola, ni fire-and-forget en
/// ningún tramo de esto — el "async" lo maneja el usuario reintentando desde la UI si hace falta.
///
/// <para>
/// HTTP-triggered, no un consumer Wolverine — no empuja correlación (guardrail de este servicio),
/// solo lee <see cref="ICorrelationContext.CorrelationId"/> ya pusheado por el middleware, igual
/// que <c>DownloadAttachmentHandler</c>.
/// </para>
///
/// <para>
/// El estado inválido para enviar (draft ya <c>Sending</c>/<c>Sent</c>/<c>Failed</c>/<c>Discarded</c>)
/// deliberadamente NO se chequea acá antes de llamar <see cref="Draft.MarkSending"/> — ese aggregate
/// ya guarda esa invariante (<c>Draft.InvalidTransition</c>) y duplicarla en el handler violaría DRY
/// sin ganar nada: el resultado es idéntico (409, Postmaster nunca llamado) tanto si se chequea acá
/// como si se deja que <see cref="Draft.MarkSending"/> lo rechace.
/// </para>
/// </summary>
public static class SendDraftHandler
{
    public static async Task<Result<SendDraftResult>> Handle(
        SendDraftCommand command,
        IDraftRepository drafts,
        IPostmasterClient postmaster,
        ICorrespondenceAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger<SendDraftCommand> logger,
        CancellationToken ct
    )
    {
        var loadResult = await LoadDraftAsync(command, drafts, ct);
        if (loadResult.IsFailure)
            return Result.Failure<SendDraftResult>(loadResult.Error);
        var draft = loadResult.Value;

        var fieldsValidation = ValidateRequiredFields(draft);
        if (fieldsValidation.IsFailure)
            return Result.Failure<SendDraftResult>(fieldsValidation.Error);

        var markSendingResult = await MarkSendingAndPersistAsync(draft, unitOfWork, ct);
        if (markSendingResult.IsFailure)
            return Result.Failure<SendDraftResult>(markSendingResult.Error);

        var sendResult = await SendViaPostmasterAsync(draft, postmaster, ct);

        return sendResult.IsSuccess
            ? await HandleSendSuccessAsync(
                command,
                draft,
                sendResult.Value,
                auditLogs,
                correlation,
                unitOfWork,
                logger,
                ct
            )
            : await HandleSendFailureAsync(
                command,
                draft,
                sendResult.Error,
                auditLogs,
                correlation,
                unitOfWork,
                logger,
                ct
            );
    }

    private static async Task<Result<Draft>> LoadDraftAsync(
        SendDraftCommand command,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.DraftId, ct);
        return draft is null
            ? Result.Failure<Draft>(new Error("Draft.NotFound", "The draft was not found for this tenant."))
            : Result.Success(draft);
    }

    /// <summary>Fail fast antes de tocar el estado del draft o llamar a Postmaster — mismo criterio que cualquier validación de entrada (plan §36 Fase 14, punto c).</summary>
    private static Result ValidateRequiredFields(Draft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Subject))
            return Result.Failure(
                new Error("SendDraftHandler.MissingRequiredFields", "Subject is required to send a draft.")
            );
        if (string.IsNullOrWhiteSpace(draft.HtmlBody))
            return Result.Failure(
                new Error("SendDraftHandler.MissingRequiredFields", "HtmlBody is required to send a draft.")
            );
        if (!draft.Recipients.Any(r => r.Type == EmailRecipientType.To))
            return Result.Failure(
                new Error(
                    "SendDraftHandler.MissingRequiredFields",
                    "At least one To recipient is required to send a draft."
                )
            );

        return Result.Success();
    }

    private static async Task<Result> MarkSendingAndPersistAsync(
        Draft draft,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var markResult = draft.MarkSending();
        if (markResult.IsFailure)
            return markResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Task<Result<SendDraftPostmasterResult>> SendViaPostmasterAsync(
        Draft draft,
        IPostmasterClient postmaster,
        CancellationToken ct
    )
    {
        var (to, cc, bcc) = SplitRecipients(draft);
        return postmaster.SendAsync(
            draft.TenantId,
            draft.Id,
            draft.AccountId,
            draft.Subject,
            draft.HtmlBody,
            draft.TextBody,
            to,
            cc,
            bcc,
            draft.Attachments.ToList(),
            draft.ReplyContext,
            ct
        );
    }

    /// <summary>
    /// Postmaster espera To/Cc/Bcc ya separados en tres listas (<c>SendCorrespondenceMessageRequest</c>)
    /// — este es el único lugar del lado de Correspondence que conoce tanto la forma de
    /// <see cref="DraftRecipient"/> (con su <see cref="EmailRecipientType"/>) como la forma de wire
    /// que espera Postmaster; ver el comentario de clase de <see cref="IPostmasterClient"/> sobre
    /// por qué esa traducción vive acá y no en el cliente.
    /// </summary>
    private static (IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc) SplitRecipients(
        Draft draft
    )
    {
        var to = new List<string>();
        var cc = new List<string>();
        var bcc = new List<string>();
        foreach (var recipient in draft.Recipients)
        {
            switch (recipient.Type)
            {
                case EmailRecipientType.To:
                    to.Add(recipient.Address);
                    break;
                case EmailRecipientType.Cc:
                    cc.Add(recipient.Address);
                    break;
                case EmailRecipientType.Bcc:
                    bcc.Add(recipient.Address);
                    break;
            }
        }

        return (to, cc, bcc);
    }

    private static async Task<Result<SendDraftResult>> HandleSendSuccessAsync(
        SendDraftCommand command,
        Draft draft,
        SendDraftPostmasterResult postmasterResult,
        ICorrespondenceAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger<SendDraftCommand> logger,
        CancellationToken ct
    )
    {
        var markSentResult = draft.MarkSent(postmasterResult.SentMessageId);
        if (markSentResult.IsFailure)
            return Result.Failure<SendDraftResult>(markSentResult.Error);

        await RecordAuditLogAsync(command, "Sent successfully.", auditLogs, correlation, logger, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new SendDraftResult(postmasterResult.SentMessageId, postmasterResult.ProviderMessageId));
    }

    /// <summary>
    /// Propaga el MISMO <see cref="Error"/> que devolvió Postmaster (o el cliente HTTP, ante un
    /// timeout/excepción de red) — nunca lo reemplaza por uno genérico. El frontend necesita ver el
    /// motivo real ("todos los destinatarios están suprimidos", no un 500 opaco) — mismo requisito
    /// UX que enviar desde Gmail (plan §36 Fase 14, punto g).
    /// </summary>
    private static async Task<Result<SendDraftResult>> HandleSendFailureAsync(
        SendDraftCommand command,
        Draft draft,
        Error error,
        ICorrespondenceAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger<SendDraftCommand> logger,
        CancellationToken ct
    )
    {
        var markFailedResult = draft.MarkFailed($"{error.Code}: {error.Message}");
        if (markFailedResult.IsFailure)
            return Result.Failure<SendDraftResult>(markFailedResult.Error);

        await RecordAuditLogAsync(
            command,
            $"Send failed ({error.Code}): {error.Message}",
            auditLogs,
            correlation,
            logger,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Failure<SendDraftResult>(error);
    }

    /// <summary>
    /// Best-effort, mismo criterio que la verificación de CloudStorage en <c>AttachFileToDraftHandler</c>:
    /// un <see cref="CorrespondenceAuditLog"/> mal formado (en la práctica, solo si
    /// <see cref="ICorrelationContext.CorrelationId"/> viniera vacío, lo que no debería pasar con el
    /// middleware ya andando) nunca debe hacer fallar el envío real que ya se resolvió — se loguea
    /// como warning en vez de descartarse en silencio (nunca ignorar un <see cref="Result"/> de
    /// dominio sin dejar rastro).
    /// </summary>
    private static async Task RecordAuditLogAsync(
        SendDraftCommand command,
        string detail,
        ICorrespondenceAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        ILogger<SendDraftCommand> logger,
        CancellationToken ct
    )
    {
        var auditResult = CorrespondenceAuditLog.Record(
            command.TenantId,
            "Send",
            "Draft",
            command.DraftId,
            command.ActorId,
            correlation.CorrelationId,
            detail
        );
        if (auditResult.IsFailure)
        {
            logger.LogWarning(
                "Could not record CorrespondenceAuditLog for draft {DraftId} ({ErrorCode}) — proceeding without it.",
                command.DraftId,
                auditResult.Error.Code
            );
            return;
        }

        await auditLogs.AddAsync(auditResult.Value, ct);
    }
}
