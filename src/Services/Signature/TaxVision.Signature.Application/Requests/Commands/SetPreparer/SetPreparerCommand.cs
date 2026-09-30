using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.SetPreparer;

/// <param name="PreparerUserId">
/// Quién es el preparer. Lo pone el controller con el usuario del JWT, no el cuerpo del request: el
/// que declara un PTIN/EFIN es el profesional que va a firmar con él. <c>null</c> solo por
/// compatibilidad con llamadores que todavía no lo manden.
/// </param>
public sealed record SetPreparerCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string PtinOrEfin,
    string DisplayName,
    string? TitleLabel,
    Guid? PreparerUserId = null
);

public static class SetPreparerHandler
{
    public static async Task<Result> Handle(
        SetPreparerCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var preparerResult = PreparerInfo.Create(cmd.PtinOrEfin, cmd.DisplayName, cmd.TitleLabel, cmd.PreparerUserId);
        if (preparerResult.IsFailure)
            return preparerResult;

        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var setResult = request.SetPreparer(preparerResult.Value);
        if (setResult.IsFailure)
            return setResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
