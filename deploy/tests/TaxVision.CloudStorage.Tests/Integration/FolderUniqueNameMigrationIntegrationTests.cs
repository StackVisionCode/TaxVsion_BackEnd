using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// La migración del índice único en una base temporal, parada en la migración anterior: con nombres repetidos
/// corta con un mensaje que dice qué hacer y no toca Files. Las anclas y la papelera no cuentan como repetidos.
/// </summary>
public sealed class FolderUniqueNameMigrationIntegrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261106212659_AddTenantStorageEntitlementsRevision";
    private readonly string _connectionString = TemporaryDatabase.ConnectionStringFor("cst_ola3_migration_it");
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = TemporaryDatabase.NewContext(_connectionString, _tenantId);
        await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    public async Task DisposeAsync()
    {
        await using var db = TemporaryDatabase.NewContext(_connectionString, _tenantId);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Duplicates_stop_the_migration_with_a_clear_message_before_touching_files()
    {
        await SeedFolderAsync("Reports");
        await SeedFolderAsync("Reports");

        await using var db = TemporaryDatabase.NewContext(_connectionString, _tenantId);
        var failure = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());

        Assert.Contains("Q-CST-6c", failure.Message, StringComparison.Ordinal);
        Assert.False(await RowVersionColumnExistsAsync("Files"));
    }

    [Fact]
    public async Task Anchors_and_the_recycle_bin_do_not_count_as_duplicates()
    {
        await SeedFolderAsync("Documents");
        await SeedFolderAsync("Documents", category: "sys.documents");
        await SeedFolderAsync("Documents", softDeleted: true);

        await using var db = TemporaryDatabase.NewContext(_connectionString, _tenantId);
        await db.Database.MigrateAsync();

        Assert.True(await RowVersionColumnExistsAsync("Files"));
        Assert.True(await RowVersionColumnExistsAsync("Folders"));
    }

    private async Task SeedFolderAsync(string name, string? category = null, bool softDeleted = false)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [Folders] ([Id], [TenantId], [OwnerType], [Name], [RelativePath], [CreatedBy], [CreatedAtUtc], [Category], [SoftDeletedAtUtc])
            VALUES (NEWID(), @tenant, 'Tenant', @name, @path, @tenant, SYSUTCDATETIME(), @category, @deleted);
            """;
        command.Parameters.AddWithValue("@tenant", _tenantId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@path", $"/{name}");
        command.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
        command.Parameters.AddWithValue("@deleted", softDeleted ? DateTime.UtcNow : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> RowVersionColumnExistsAsync(string table)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COL_LENGTH(@table, 'RowVersion')";
        command.Parameters.AddWithValue("@table", table);
        return await command.ExecuteScalarAsync() is not DBNull;
    }
}
