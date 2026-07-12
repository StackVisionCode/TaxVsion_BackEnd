using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Commands.RemoveField;

public static class RemoveFieldHandler
{
    public static async Task<Result> Handle(
        RemoveFieldCommand cmd,
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

        var result = request.RemoveField(cmd.SignerId, cmd.FieldId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
