using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateMetadata;

public static class UpdateTemplateMetadataHandler
{
    public static async Task<Result> Handle(
        UpdateTemplateMetadataCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure(category.Error);

        var result = template.UpdateMetadata(cmd.Title, cmd.Description, category.Value);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
