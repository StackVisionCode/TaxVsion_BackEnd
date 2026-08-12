using TaxVision.Catalog.Application.Categories;
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
            new CreateCategoryCommand(Tenant, User, "Child", null, Guid.NewGuid()), repo, new FakeUnitOfWork(), CancellationToken.None
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
            new CreateCategoryCommand(Tenant, User, "Services", "desc", null), repo, uow, CancellationToken.None
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
        var result = await DeleteCategoryHandler.Handle(new DeleteCategoryCommand(Tenant, cat.Id), repo, new FakeUnitOfWork(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.CategoryHasChildren.Code, result.Error.Code);
    }

    [Fact]
    public async Task Delete_without_children_soft_deletes()
    {
        var repo = new FakeCategoryRepository { HasChildrenResult = false };
        var cat = Category.Create(Tenant, User, "Leaf", null, null, Now).Value;
        repo.Seed(cat);
        var result = await DeleteCategoryHandler.Handle(new DeleteCategoryCommand(Tenant, cat.Id), repo, new FakeUnitOfWork(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(cat.IsDeleted);
    }

    [Fact]
    public async Task List_returns_tenant_categories()
    {
        var repo = new FakeCategoryRepository();
        repo.Seed(Category.Create(Tenant, User, "A", null, null, Now).Value);
        repo.Seed(Category.Create(Tenant, User, "B", null, null, Now).Value);
        var result = await ListCategoriesHandler.Handle(new ListCategoriesQuery(Tenant, false), repo, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }
}
