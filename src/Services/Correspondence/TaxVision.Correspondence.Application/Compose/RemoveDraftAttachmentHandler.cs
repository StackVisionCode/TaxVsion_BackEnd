using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 12 — HTTP-triggered, no un consumer Wolverine, mismo criterio que el resto de los
/// handlers de este servicio (no empuja correlación). <see cref="Domain.Compose.Draft.RemoveAttachment"/>
/// ya es idempotente/tolerante ante un <c>fileId</c> que nunca estuvo adjunto (no-op exitoso) —
/// este handler no duplica ese chequeo, solo lo propaga.
/// </summary>
public static class RemoveDraftAttachmentHandler
{
    public static async Task<Result> Handle(
        RemoveDraftAttachmentCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.DraftId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The draft was not found for this tenant."));

        var removeResult = draft.RemoveAttachment(command.FileId);
        if (removeResult.IsFailure)
            return removeResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
