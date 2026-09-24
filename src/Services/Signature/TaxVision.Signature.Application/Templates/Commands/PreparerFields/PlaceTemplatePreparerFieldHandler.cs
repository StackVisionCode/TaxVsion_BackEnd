using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.PreparerFields;

public static class PlaceTemplatePreparerFieldHandler
{
    public static async Task<Result<TemplatePreparerFieldCreatedResponse>> Handle(
        PlaceTemplatePreparerFieldCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var positionResult = FieldPosition.Create(cmd.Page, cmd.X, cmd.Y, cmd.Width, cmd.Height);
        if (positionResult.IsFailure)
            return Result.Failure<TemplatePreparerFieldCreatedResponse>(positionResult.Error);

        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure<TemplatePreparerFieldCreatedResponse>(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var placement = template.PlacePreparerField(cmd.Kind, positionResult.Value, cmd.Label);
        if (placement.IsFailure)
            return Result.Failure<TemplatePreparerFieldCreatedResponse>(placement.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new TemplatePreparerFieldCreatedResponse(placement.Value.Id));
    }
}
