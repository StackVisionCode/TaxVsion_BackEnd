using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Crea un <see cref="Draft"/> vacío para correspondencia nueva (Fase 11) — HTTP-triggered, no un
/// consumer Wolverine, mismo criterio que el resto de los handlers de este servicio (no empuja
/// correlación). A diferencia de <see cref="StartReplyHandler"/>, nunca reutiliza un draft
/// existente: cada llamada crea uno nuevo, el usuario decide cuándo abrir una redacción en blanco.
/// </summary>
public static class CreateDraftHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateDraftCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draftResult = Draft.CreateNew(command.TenantId, command.CustomerId, command.AccountId);
        if (draftResult.IsFailure)
            return Result.Failure<Guid>(draftResult.Error);

        var draft = draftResult.Value;
        await drafts.AddAsync(draft, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(draft.Id);
    }
}
