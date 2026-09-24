using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories.Commands.Archive;
using TaxVision.Signature.Application.Categories.Commands.Create;
using TaxVision.Signature.Application.Categories.Commands.Rename;
using TaxVision.Signature.Application.Categories.Queries.List;
using TaxVision.Signature.Domain.Categories;
using Xunit;

namespace TaxVision.Signature.Tests.Application;

public sealed class SignatureCategoryHandlersTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Create_adds_a_custom_category()
    {
        var repo = new FakeRepository();

        var result = await CreateSignatureCategoryHandler.Handle(
            new CreateSignatureCategoryCommand(Tenant, User, "Payroll"),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Payroll", result.Value.Name);
        Assert.False(result.Value.IsSystem);
        Assert.Single(repo.Added);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        var repo = new FakeRepository();
        repo.Seed(TenantSignatureCategory.Create(Tenant, User, "Payroll").Value);

        var result = await CreateSignatureCategoryHandler.Handle(
            new CreateSignatureCategoryCommand(Tenant, User, "  payroll "),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Category.Duplicate", result.Error.Code);
    }

    [Fact]
    public async Task Rename_returns_not_found_when_missing()
    {
        var result = await RenameSignatureCategoryHandler.Handle(
            new RenameSignatureCategoryCommand(Tenant, Guid.NewGuid(), "X Y"),
            new FakeRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Category.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Archive_soft_archives_the_category()
    {
        var repo = new FakeRepository();
        var category = TenantSignatureCategory.Create(Tenant, User, "Payroll").Value;
        repo.Seed(category);

        var result = await SetSignatureCategoryArchivedHandler.Handle(
            new SetSignatureCategoryArchivedCommand(Tenant, category.Id, Archived: true),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(category.IsArchived);
    }

    [Fact]
    public async Task List_merges_system_and_custom_categories()
    {
        var repo = new FakeRepository();
        repo.Seed(TenantSignatureCategory.Create(Tenant, User, "Payroll").Value);

        var result = await ListSignatureCategoriesHandler.Handle(
            new ListSignatureCategoriesQuery(Tenant, IncludeArchived: false),
            repo,
            CancellationToken.None
        );

        Assert.Contains(result.Categories, c => c.IsSystem && c.Name == "Fiscal");
        Assert.Contains(result.Categories, c => !c.IsSystem && c.Name == "Payroll");
        // Las de sistema son exactamente los defaults.
        Assert.Equal(SignatureCategoryDefaults.Names.Count, result.Categories.Count(c => c.IsSystem));
    }

    private sealed class FakeRepository : ITenantSignatureCategoryRepository
    {
        private readonly List<TenantSignatureCategory> store = [];
        public IReadOnlyList<TenantSignatureCategory> Added => store;

        public void Seed(TenantSignatureCategory category) => store.Add(category);

        public Task<TenantSignatureCategory?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(store.FirstOrDefault(c => c.TenantId == tenantId && c.Id == id));

        public Task<IReadOnlyList<TenantSignatureCategory>> ListAsync(
            Guid tenantId,
            bool includeArchived,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<TenantSignatureCategory>>(
                store.Where(c => c.TenantId == tenantId && (includeArchived || !c.IsArchived)).ToList()
            );

        public Task<bool> ExistsByNormalizedNameAsync(
            Guid tenantId,
            string normalizedName,
            Guid? excludingId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                store.Any(c =>
                    c.TenantId == tenantId
                    && c.NormalizedName == normalizedName
                    && (excludingId == null || c.Id != excludingId)
                )
            );

        public Task AddAsync(TenantSignatureCategory category, CancellationToken ct = default)
        {
            store.Add(category);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
