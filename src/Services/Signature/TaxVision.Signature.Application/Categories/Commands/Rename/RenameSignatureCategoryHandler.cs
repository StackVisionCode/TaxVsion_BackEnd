using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Application.Categories.Commands.Rename;

public static class RenameSignatureCategoryHandler
{
    public static async Task<Result> Handle(
        RenameSignatureCategoryCommand cmd,
        ITenantSignatureCategoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var category = await repository.GetByIdAsync(cmd.TenantId, cmd.CategoryId, ct);
        if (category is null)
            return NotFound();

        var normalized = TenantSignatureCategory.Normalize(cmd.Name);
        if (await repository.ExistsByNormalizedNameAsync(cmd.TenantId, normalized, excludingId: cmd.CategoryId, ct))
            return Result.Failure(
                new Error("Signature.Category.Duplicate", "A category with that name already exists.")
            );

        var rename = category.Rename(cmd.Name);
        if (rename.IsFailure)
            return rename;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(new Error("Signature.Category.NotFound", "The category does not exist for this tenant."));
}
