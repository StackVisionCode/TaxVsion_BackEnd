using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

public static class PlacePreparerFieldHandler
{
    public static async Task<Result<PreparerFieldResponse>> Handle(
        PlacePreparerFieldCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var positionResult = FieldPosition.Create(cmd.Page, cmd.X, cmd.Y, cmd.Width, cmd.Height);
        if (positionResult.IsFailure)
            return Result.Failure<PreparerFieldResponse>(positionResult.Error);

        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<PreparerFieldResponse>(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var placement = request.PlacePreparerField(cmd.Kind, positionResult.Value, cmd.Label);
        if (placement.IsFailure)
            return Result.Failure<PreparerFieldResponse>(placement.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var field = placement.Value;
        return Result.Success(
            new PreparerFieldResponse(
                field.Id,
                field.Kind,
                field.Position.Page,
                field.Position.X,
                field.Position.Y,
                field.Position.Width,
                field.Position.Height,
                field.Label
            )
        );
    }
}
