using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Audit;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Cierra un borrador que quedó en <see cref="DraftStatus.Sending"/> (COR-08): repite la llamada, y Postmaster responde
/// con el envío existente por la clave del borrador, sin reenviar. Sin respuesta clara se espera; pasado el plazo se
/// cierra como resultado desconocido. Nunca se manda de nuevo a ciegas.
/// </summary>
public sealed class DraftSendReconciler(
    IDraftRepository drafts,
    IPostmasterClient postmaster,
    ICorrespondenceAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IOptions<DraftSendReconciliationOptions> options,
    ILogger<DraftSendReconciler> logger
)
{
    public const string OutcomeUnknownCode = "Draft.SendOutcomeUnknown";

    public async Task ReconcileAsync(Guid tenantId, Guid draftId, CancellationToken ct = default)
    {
        var draft = await drafts.GetByIdAsync(tenantId, draftId, ct);
        if (draft is not { Status: DraftStatus.Sending })
            return;

        var result = await SendDraftHandler.SendViaPostmasterAsync(draft, postmaster, ct);
        string detail;
        Result closed;
        if (result is { IsSuccess: true, Value.InProgress: false })
        {
            closed = draft.MarkSent(result.Value.SentMessageId, result.Value.InternetMessageId);
            detail = "Sent successfully (reconciled).";
        }
        else if (result.IsFailure && !SendDraftHandler.IsOutcomeUnknown(result.Error))
        {
            closed = draft.MarkFailed($"{result.Error.Code}: {result.Error.Message}");
            detail = $"Send failed ({result.Error.Code}): {result.Error.Message}";
        }
        else if (DateTime.UtcNow - draft.UpdatedAtUtc >= options.Value.GiveUpAfter)
        {
            closed = draft.MarkFailed($"{OutcomeUnknownCode}: The result of this send could not be confirmed.");
            detail = "Send closed without a confirmed result.";
        }
        else
        {
            return;
        }

        if (closed.IsFailure)
        {
            logger.LogWarning("Could not close draft {DraftId}: {ErrorCode}.", draftId, closed.Error.Code);
            return;
        }

        var audit = CorrespondenceAuditLog.Record(
            tenantId,
            "Send",
            "Draft",
            draftId,
            draft.CreatedByUserId,
            Guid.NewGuid().ToString("N"),
            detail
        );
        if (audit.IsSuccess)
            await auditLogs.AddAsync(audit.Value, ct);
        else
            logger.LogWarning(
                "Could not record CorrespondenceAuditLog for draft {DraftId} ({ErrorCode}).",
                draftId,
                audit.Error.Code
            );

        await unitOfWork.SaveChangesAsync(ct);
    }
}
