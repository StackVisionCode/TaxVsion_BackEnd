using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Application.Common;
using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Categories;

namespace TaxVision.Catalog.Application.Categories;

// ───────────────────────── Commands ─────────────────────────

public sealed record CreateCategoryCommand(
    Guid TenantId,
    Guid TaxUserId,
    string Name,
    string? Description,
    Guid? ParentCategoryId
);

public sealed record UpdateCategoryCommand(Guid TenantId, Guid Id, string Name, string? Description, Guid? ParentCategoryId);

public sealed record SetCategoryActiveCommand(Guid TenantId, Guid Id, bool IsActive);

public sealed record DeleteCategoryCommand(Guid TenantId, Guid Id);

public static class CreateCategoryHandler
{
    public static async Task<Result<CategoryDto>> Handle(
        CreateCategoryCommand command,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.ParentCategoryId is { } parent
            && parent != Guid.Empty
            && !await categories.ExistsAsync(command.TenantId, parent, ct))
            return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);

        var created = Category.Create(
            command.TenantId, command.TaxUserId, command.Name, command.Description, command.ParentCategoryId, DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<CategoryDto>(created.Error);

        await categories.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(created.Value.ToDto());
    }
}

public static class UpdateCategoryHandler
{
    public static async Task<Result<CategoryDto>> Handle(
        UpdateCategoryCommand command,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var category = await categories.GetByIdAsync(command.TenantId, command.Id, ct);
        if (category is null)
            return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);

        if (command.ParentCategoryId is { } parent
            && parent != Guid.Empty
            && !await categories.ExistsAsync(command.TenantId, parent, ct))
            return Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound);

        var updated = category.Update(command.Name, command.Description, command.ParentCategoryId, DateTime.UtcNow);
        if (updated.IsFailure)
            return Result.Failure<CategoryDto>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(category.ToDto());
    }
}

public static class SetCategoryActiveHandler
{
    public static async Task<Result> Handle(
        SetCategoryActiveCommand command,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var category = await categories.GetByIdAsync(command.TenantId, command.Id, ct);
        if (category is null)
            return Result.Failure(CatalogErrors.CategoryNotFound);

        category.SetActive(command.IsActive, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class DeleteCategoryHandler
{
    public static async Task<Result> Handle(
        DeleteCategoryCommand command,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var category = await categories.GetByIdAsync(command.TenantId, command.Id, ct);
        if (category is null)
            return Result.Failure(CatalogErrors.CategoryNotFound);

        // No se borra una categoría con subcategorías o ítems (integridad del árbol/catálogo).
        if (await categories.HasChildrenAsync(command.TenantId, command.Id, ct))
            return Result.Failure(CatalogErrors.CategoryHasChildren);

        category.SoftDelete(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ───────────────────────── Queries ─────────────────────────

public sealed record GetCategoryQuery(Guid TenantId, Guid Id);

public sealed record ListCategoriesQuery(Guid TenantId, bool ActiveOnly);

public static class GetCategoryHandler
{
    public static async Task<Result<CategoryDto>> Handle(
        GetCategoryQuery query,
        ICategoryRepository categories,
        CancellationToken ct
    )
    {
        var category = await categories.GetByIdAsync(query.TenantId, query.Id, ct);
        return category is null
            ? Result.Failure<CategoryDto>(CatalogErrors.CategoryNotFound)
            : Result.Success(category.ToDto());
    }
}

public static class ListCategoriesHandler
{
    public static async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        ListCategoriesQuery query,
        ICategoryRepository categories,
        CancellationToken ct
    )
    {
        var rows = await categories.ListAsync(query.TenantId, query.ActiveOnly, ct);
        IReadOnlyList<CategoryDto> dtos = rows.Select(c => c.ToDto()).ToList();
        return Result.Success(dtos);
    }
}
