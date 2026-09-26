using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Application.Categories.Commands.Create;

/// <summary>Crea una categoría custom del tenant. El dominio rechaza nombres reservados de sistema; aquí se deduplica contra las custom.</summary>
public static class CreateSignatureCategoryHandler
{
    public static async Task<Result<SignatureCategoryResponse>> Handle(
        CreateSignatureCategoryCommand cmd,
        ITenantSignatureCategoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var creation = TenantSignatureCategory.Create(cmd.TenantId, cmd.CreatedByUserId, cmd.Name);
        if (creation.IsFailure)
            return Result.Failure<SignatureCategoryResponse>(creation.Error);

        var category = creation.Value;
        if (await repository.ExistsByNormalizedNameAsync(cmd.TenantId, category.NormalizedName, excludingId: null, ct))
            return Result.Failure<SignatureCategoryResponse>(Duplicate());

        await repository.AddAsync(category, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new SignatureCategoryResponse(category.Id, category.Name, IsSystem: false, category.IsArchived)
        );
    }

    private static Error Duplicate() =>
        new("Signature.Category.Duplicate", "A category with that name already exists.");
}
