using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Categories.Commands.Archive;

/// <summary>Archiva (soft) o restaura una categoría custom. No se borra en firme para no afectar el histórico.</summary>
public static class SetSignatureCategoryArchivedHandler
{
    public static async Task<Result> Handle(
        SetSignatureCategoryArchivedCommand cmd,
        ITenantSignatureCategoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var category = await repository.GetByIdAsync(cmd.TenantId, cmd.CategoryId, ct);
        if (category is null)
            return Result.Failure(
                new Error("Signature.Category.NotFound", "The category does not exist for this tenant.")
            );

        var result = cmd.Archived ? category.Archive() : category.Unarchive();
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
