using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.PlaceField;

public static class PlaceTemplateFieldHandler
{
    public static async Task<Result<TemplateFieldCreatedResponse>> Handle(
        PlaceTemplateFieldCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var positionResult = FieldPosition.Create(cmd.Page, cmd.X, cmd.Y, cmd.Width, cmd.Height);
        if (positionResult.IsFailure)
            return Result.Failure<TemplateFieldCreatedResponse>(positionResult.Error);

        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure<TemplateFieldCreatedResponse>(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var placement = template.PlaceField(cmd.SlotOrder, cmd.Kind, positionResult.Value, cmd.Label, cmd.IsRequired);
        if (placement.IsFailure)
            return Result.Failure<TemplateFieldCreatedResponse>(placement.Error);

        await unitOfWork.SaveChangesAsync(ct);
        var field = placement.Value;
        return Result.Success(new TemplateFieldCreatedResponse(field.Id, field.SlotOrder));
    }
}
