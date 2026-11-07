using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>SIG-20: una instalación limpia migra hasta el final, y una base vieja sigue pasando por el rename.</summary>
public sealed class SignatureMigrationsIntegrationTests
{
    private const string BeforeRename = "20261004034140_AddSignerDocumentFirstViewedAtUtc";
    private const string OldColumn = "SendSignedDocumentToSigners";
    private const string NewColumn = "SendSealedDocumentToSigners";

    [Fact]
    public async Task Empty_database_migrates_to_the_end()
    {
        var database = new SignatureTemporaryDatabase();
        try
        {
            await using var db = database.NewContext(Guid.Empty);

            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.True(await ColumnExistsAsync(database, NewColumn));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    // La base que nació con el nombre viejo de la columna: el rename tiene que seguir ocurriendo.
    [Fact]
    public async Task Database_with_the_old_column_still_gets_it_renamed()
    {
        var database = new SignatureTemporaryDatabase();
        try
        {
            await using var db = database.NewContext(Guid.Empty);
            await db.GetService<IMigrator>().MigrateAsync(BeforeRename);
            await db.Database.ExecuteSqlRawAsync(
                $"EXEC sp_rename N'SignatureRequests.{NewColumn}', N'{OldColumn}', N'COLUMN';"
            );

            await db.Database.MigrateAsync();

            Assert.True(await ColumnExistsAsync(database, NewColumn));
            Assert.False(await ColumnExistsAsync(database, OldColumn));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(SignatureTemporaryDatabase database, string column)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.columns WHERE Name = @name AND Object_ID = OBJECT_ID(N'SignatureRequests')";
        command.Parameters.AddWithValue("@name", column);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }
}
