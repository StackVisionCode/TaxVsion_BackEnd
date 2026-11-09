using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxVision.Catalog.Application.Categories;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Infrastructure.Persistence;
using TaxVision.Catalog.Infrastructure.Persistence.Repositories;

namespace TaxVision.Catalog.Tests.Integration;

/// <summary>CAT-3 con el repositorio real y SQL real, en una base temporal propia que se borra al terminar.</summary>
public sealed class CategoryCycleIntegrationTests : IAsyncLifetime
{
    private const string ApiUserSecretsId = "c7d4e2b8-1a6f-4d3c-8e29-5b7a1c4f9d63";
    private const string Prefix = "catcyc_";

    private readonly string _connectionString;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CategoryCycleIntegrationTests()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("CATALOG_DB_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set CATALOG_DB_CONNECTION or the Catalog user-secrets.");
        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Ancestors_AreReturnedFromTheParentUp()
    {
        var root = await SeedAsync("Root", null);
        var child = await SeedAsync("Child", root.Id);
        var leaf = await SeedAsync("Leaf", child.Id);

        await using var db = NewContext();
        var ancestors = await new CategoryRepository(db).GetAncestorIdsAsync(_tenantId, leaf.Id, 50);

        Assert.Equal([child.Id, root.Id], ancestors);
    }

    [Fact]
    public async Task Ancestors_OfAnotherTenant_AreNotVisible()
    {
        var root = await SeedAsync("Root", null);

        await using var db = NewContext();
        var ancestors = await new CategoryRepository(db).GetAncestorIdsAsync(Guid.NewGuid(), root.Id, 50);

        Assert.Empty(ancestors);
    }

    [Fact]
    public async Task Ancestors_StopAtTheLimitOnAnExistingCycle()
    {
        var first = await SeedAsync("First", null);
        var second = await SeedAsync("Second", first.Id);
        await using (var edit = NewContext())
        {
            var stored = await edit.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == first.Id);
            stored.Update(stored.Name, null, second.Id, DateTime.UtcNow);
            await edit.SaveChangesAsync();
        }

        await using var db = NewContext();
        var ancestors = await new CategoryRepository(db).GetAncestorIdsAsync(_tenantId, second.Id, 5);

        Assert.Equal(5, ancestors.Count);
    }

    [Fact]
    public async Task Update_creating_a_cycle_leaves_the_row_untouched()
    {
        var root = await SeedAsync("Root", null);
        var child = await SeedAsync("Child", root.Id);

        await using (var db = NewContext())
        {
            var result = await UpdateCategoryHandler.Handle(
                new UpdateCategoryCommand(_tenantId, root.Id, "Root", null, child.Id),
                new CategoryRepository(db),
                db,
                Options.Create(new CategoryTreeOptions()),
                CancellationToken.None
            );

            Assert.Equal("catalog.categoryCycle", result.Error.Code);
        }

        await using var read = NewContext();
        var stored = await read.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == root.Id);
        Assert.Null(stored.ParentCategoryId);
    }

    private async Task<Category> SeedAsync(string name, Guid? parentId)
    {
        var category = Category.Create(_tenantId, Guid.NewGuid(), name, null, parentId, DateTime.UtcNow).Value;
        await using var db = NewContext();
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private CatalogDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CatalogDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
