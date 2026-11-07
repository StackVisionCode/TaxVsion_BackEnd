using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;
using TaxVision.Catalog.Domain.ValueObjects;
using TaxVision.Catalog.Infrastructure.Persistence;

namespace TaxVision.Catalog.Tests.Integration;

/// <summary>CAT-2 con SQL real, en una base temporal propia: dos guardados de la misma lectura, el segundo choca.</summary>
public sealed class CatalogItemConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string ApiUserSecretsId = "c7d4e2b8-1a6f-4d3c-8e29-5b7a1c4f9d63";
    private const string Prefix = "catint_";
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly string _connectionString;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public CatalogItemConcurrencyIntegrationTests()
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
    public async Task TwoEdits_FromSameRead_SecondGetsConflict()
    {
        var item = await SeedItemAsync();
        await using var winnerDb = NewContext();
        await using var loserDb = NewContext();
        var winner = await winnerDb.CatalogItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id);
        var loser = await loserDb.CatalogItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id);
        Edit(winner, "First");
        Edit(loser, "Second");

        await winnerDb.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loserDb.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = NewContext();
        Assert.Equal(
            "First",
            (await read.CatalogItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id)).Description
        );
    }

    [Fact]
    public async Task Category_TwoEdits_FromSameRead_SecondGetsConflict()
    {
        var category = await SeedCategoryAsync();
        await using var winnerDb = NewContext();
        await using var loserDb = NewContext();
        var winner = await winnerDb.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == category.Id);
        var loser = await loserDb.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == category.Id);
        winner.Update("First", null, null, DateTime.UtcNow);
        loser.Update("Second", null, null, DateTime.UtcNow);

        await winnerDb.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loserDb.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    private static void Edit(CatalogItem item, string description) =>
        item.Update(
            item.Name,
            description,
            item.Barcode,
            item.CategoryId,
            item.Unit,
            item.TaxRateBasisPoints,
            item.ImageUrl,
            item.TrackInventory,
            DateTime.UtcNow
        );

    private async Task<Category> SeedCategoryAsync()
    {
        var category = Category.Create(_tenantId, _user, "Hardware", null, null, Now).Value;
        await using var db = NewContext();
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private async Task<CatalogItem> SeedItemAsync()
    {
        var category = await SeedCategoryAsync();
        var item = CatalogItem
            .Create(
                _tenantId,
                _user,
                "Widget",
                null,
                "SKU-1",
                null,
                category.Id,
                ItemKind.Product,
                Money.Create(100, "USD").Value,
                null,
                null,
                0,
                true,
                null,
                Now
            )
            .Value;
        await using var db = NewContext();
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return item;
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
