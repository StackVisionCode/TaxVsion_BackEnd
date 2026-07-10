using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.PlaceField;

public static class PlaceFieldHandler
{
    public static async Task<Result<SignatureFieldResponse>> Handle(
        PlaceFieldCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var positionResult = FieldPosition.Create(cmd.Page, cmd.X, cmd.Y, cmd.Width, cmd.Height);
        if (positionResult.IsFailure)
            return Result.Failure<SignatureFieldResponse>(positionResult.Error);

        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<SignatureFieldResponse>(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var placement = request.PlaceField(cmd.SignerId, cmd.Kind, positionResult.Value, cmd.Label, cmd.IsRequired);
        if (placement.IsFailure)
            return Result.Failure<SignatureFieldResponse>(placement.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(cmd.SignerId, placement.Value));
    }

    private static SignatureFieldResponse Map(Guid signerId, SignatureField field) =>
        new(
            field.Id,
            signerId,
            field.Kind,
            field.Position.Page,
            field.Position.X,
            field.Position.Y,
            field.Position.Width,
            field.Position.Height,
            field.Label,
            field.IsRequired
        );
}
