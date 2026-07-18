using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// <c>DELETE /correspondence/drafts/{id}</c> (Fase 11) — HTTP-triggered, no un consumer
/// Wolverine, mismo criterio que el resto de los handlers de este servicio (no empuja
/// correlación).
///
/// <para>
/// A diferencia de <see cref="Domain.Inbox.EmailThread.Archive"/> (idempotente por diseño DENTRO
/// del aggregate — ver <see cref="Threads.ArchiveThreadHandler"/>), <see cref="Draft.Discard"/> es
/// deliberadamente estricto: solo es válido desde <see cref="DraftStatus.Draft"/> (ver el
/// comentario de clase de <see cref="Draft"/> sobre por qué — una vez <c>Sending</c>/<c>Sent</c> no
/// existe "des-enviar"). Este handler NO toca esa invariante del aggregate. En cambio, aplica
/// idempotencia solo en el caso que es genuinamente el mismo estado final que ya se pidió — llamar
/// DELETE dos veces sobre el mismo draft ya descartado es un no-op exitoso (mismo resultado neto
/// que pedía el primer DELETE) — pero llamar DELETE sobre un draft <c>Sending</c>/<c>Sent</c>/
/// <c>Failed</c> propaga el 409 real de <c>Draft.InvalidTransition</c>: ahí no es "la misma acción
/// repetida", es una acción que nunca fue válida para ese draft.
/// </para>
/// </summary>
public static class DiscardDraftHandler
{
    public static async Task<Result> Handle(
        DiscardDraftCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.DraftId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The draft was not found for this tenant."));

        if (draft.Status == DraftStatus.Discarded)
            return Result.Success();

        var discardResult = draft.Discard();
        if (discardResult.IsFailure)
            return discardResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
