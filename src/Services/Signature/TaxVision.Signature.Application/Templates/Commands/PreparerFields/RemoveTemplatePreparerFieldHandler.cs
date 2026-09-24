using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.PreparerFields;

public static class RemoveTemplatePreparerFieldHandler
{
    public static async Task<Result> Handle(
        RemoveTemplatePreparerFieldCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var removal = template.RemovePreparerField(cmd.FieldId);
        if (removal.IsFailure)
            return removal;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
