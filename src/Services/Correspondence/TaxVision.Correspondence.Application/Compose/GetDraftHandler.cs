using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Lectura completa de UN draft (Fase 11) — HTTP-triggered, no un consumer Wolverine, mismo
/// criterio que <see cref="Messages.GetMessageMetadataHandler"/> (no empuja correlación, nunca
/// llama a Connectors/CloudStorage: puro mapeo de lo ya persistido).
/// </summary>
public static class GetDraftHandler
{
    public static async Task<Result<DraftDetail>> Handle(
        GetDraftQuery query,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(query.TenantId, query.DraftId, ct);
        return draft is null
            ? Result.Failure<DraftDetail>(new Error("Draft.NotFound", "The draft was not found for this tenant."))
            : Result.Success(ToDetail(draft));
    }

    internal static DraftDetail ToDetail(Draft draft) =>
        new(
            draft.Id,
            draft.CustomerId,
            draft.AccountId,
            draft.Subject,
            draft.HtmlBody,
            draft.TextBody,
            draft.Status.ToString(),
            draft
                .Recipients.Select(r => new DraftRecipientSummary(r.Address, r.Type.ToString(), r.DisplayName))
                .ToList(),
            draft
                .Attachments.Select(a => new DraftAttachmentSummary(a.FileId, a.Filename, a.ContentType, a.SizeBytes))
                .ToList(),
            draft.ReplyContext,
            draft.SentMessageId,
            draft.FailureReason,
            draft.CreatedAtUtc,
            draft.UpdatedAtUtc,
            draft.LastAutoSavedAtUtc
        );
}
