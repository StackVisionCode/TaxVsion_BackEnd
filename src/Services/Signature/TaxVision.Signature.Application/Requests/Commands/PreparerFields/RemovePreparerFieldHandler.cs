using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

public static class RemovePreparerFieldHandler
{
    public static async Task<Result> Handle(
        RemovePreparerFieldCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var removal = request.RemovePreparerField(cmd.FieldId);
        if (removal.IsFailure)
            return removal;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
