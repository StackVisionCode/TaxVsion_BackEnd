using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Catalog.Application.Categories;
using TaxVision.Catalog.Application.Common;
using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Tests.Fakes;

namespace TaxVision.Catalog.Tests.Application;

public sealed class CategoryHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_with_missing_parent_fails()
    {
        var repo = new FakeCategoryRepository();
        var result = await CreateCategoryHandler.Handle(
            new CreateCategoryCommand(Tenant, User, "Child", null, Guid.NewGuid()),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.CategoryNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_root_category_succeeds()
    {
        var repo = new FakeCategoryRepository();
        var uow = new FakeUnitOfWork();
        var result = await CreateCategoryHandler.Handle(
            new CreateCategoryCommand(Tenant, User, "Services", "desc", null),
            repo,
            uow,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal("Services", result.Value.Name);
        Assert.Single(repo.Added);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Delete_with_children_returns_conflict()
    {
        var repo = new FakeCategoryRepository { HasChildrenResult = true };
        var cat = Category.Create(Tenant, User, "Parent", null, null, Now).Value;
        repo.Seed(cat);
        var result = await DeleteCategoryHandler.Handle(
            new DeleteCategoryCommand(Tenant, cat.Id),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.CategoryHasChildren.Code, result.Error.Code);
    }

    [Fact]
    public async Task Delete_without_children_soft_deletes()
    {
        var repo = new FakeCategoryRepository { HasChildrenResult = false };
        var cat = Category.Create(Tenant, User, "Leaf", null, null, Now).Value;
        repo.Seed(cat);
        var result = await DeleteCategoryHandler.Handle(
            new DeleteCategoryCommand(Tenant, cat.Id),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.True(cat.IsDeleted);
    }

    [Fact]
    public async Task Update_creating_two_level_cycle_fails()
    {
        var repo = new FakeCategoryRepository();
        var root = Seed(repo, "A", null);
        var child = Seed(repo, "B", root.Id);

        var result = await Update(repo, root, child.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.categoryCycle", result.Error.Code);
        Assert.Null(root.ParentCategoryId);
    }

    [Fact]
    public async Task Update_creating_three_level_cycle_fails()
    {
        var repo = new FakeCategoryRepository();
        var root = Seed(repo, "A", null);
        var child = Seed(repo, "B", root.Id);
        var grandchild = Seed(repo, "C", child.Id);

        var result = await Update(repo, root, grandchild.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.categoryCycle", result.Error.Code);
        Assert.Null(root.ParentCategoryId);
    }

    [Fact]
    public async Task Update_moving_under_unrelated_branch_succeeds()
    {
        var repo = new FakeCategoryRepository();
        Seed(repo, "A", null);
        var target = Seed(repo, "B", null);
        var moved = Seed(repo, "C", null);

        var result = await Update(repo, moved, target.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(target.Id, moved.ParentCategoryId);
    }

    [Fact]
    public async Task Update_under_a_branch_that_already_has_a_cycle_fails()
    {
        // Ciclo viejo en los datos: subir por él nunca termina, el tope lo corta.
        var repo = new FakeCategoryRepository();
        var first = Seed(repo, "A", null);
        var second = Seed(repo, "B", first.Id);
        first.Update("A", null, second.Id, Now);
        var moved = Seed(repo, "C", null);

        var result = await Update(repo, moved, second.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.categoryCycle", result.Error.Code);
        Assert.Null(moved.ParentCategoryId);
    }

    [Fact]
    public async Task Update_keeping_the_same_parent_succeeds()
    {
        var repo = new FakeCategoryRepository();
        var root = Seed(repo, "A", null);
        var child = Seed(repo, "B", root.Id);

        var result = await Update(repo, child, root.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task List_returns_tenant_categories()
    {
        var repo = new FakeCategoryRepository();
        repo.Seed(Category.Create(Tenant, User, "A", null, null, Now).Value);
        repo.Seed(Category.Create(Tenant, User, "B", null, null, Now).Value);
        var result = await ListCategoriesHandler.Handle(
            new ListCategoriesQuery(Tenant, false),
            repo,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    private static Category Seed(FakeCategoryRepository repo, string name, Guid? parentId)
    {
        var category = Category.Create(Tenant, User, name, null, parentId, Now).Value;
        repo.Seed(category);
        return category;
    }

    private static Task<Result<CategoryDto>> Update(FakeCategoryRepository repo, Category category, Guid? parentId) =>
        UpdateCategoryHandler.Handle(
            new UpdateCategoryCommand(Tenant, category.Id, category.Name, null, parentId),
            repo,
            new FakeUnitOfWork(),
            Options.Create(new CategoryTreeOptions()),
            CancellationToken.None
        );
}
