using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.SetPreparer;

public sealed record SetPreparerCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string PtinOrEfin,
    string DisplayName,
    string? TitleLabel
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
        var preparerResult = PreparerInfo.Create(cmd.PtinOrEfin, cmd.DisplayName, cmd.TitleLabel);
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
