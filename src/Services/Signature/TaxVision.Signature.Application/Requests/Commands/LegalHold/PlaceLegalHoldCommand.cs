using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Commands.LegalHold;

public sealed record PlaceLegalHoldCommand(Guid TenantId, Guid SignatureRequestId, Guid PlacedByUserId, string Reason);

public static class PlaceLegalHoldHandler
{
    public static async Task<Result> Handle(
        PlaceLegalHoldCommand cmd,
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

        var result = request.PlaceLegalHold(cmd.PlacedByUserId, cmd.Reason);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
