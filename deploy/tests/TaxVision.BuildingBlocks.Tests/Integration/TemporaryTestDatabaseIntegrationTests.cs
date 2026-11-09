using Microsoft.Data.SqlClient;
using TaxVision.BuildingBlocks.Testing.Integration;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>BB-10: la base temporal contra SQL real. La borra el framework al terminar la corrida.</summary>
public sealed class TemporaryTestDatabaseIntegrationTests
{
    private readonly string _prefix = $"bbt_{Guid.NewGuid():N}"[..12];

    [Fact]
    public async Task Database_IsItsOwnAndTheSchemaRunsOnIt()
    {
        var database = NewDatabase(connection =>
        {
            TemporaryTestDatabase.CreateEmpty(connection);
            using var sql = new SqlConnection(connection);
            sql.Open();
            using var command = sql.CreateCommand();
            command.CommandText = "CREATE TABLE dbo.Probe (Id int);";
            command.ExecuteNonQuery();
        });

        Assert.StartsWith(_prefix + "_", Catalog(database));
        Assert.NotEqual(Catalog(database), new SqlConnectionStringBuilder(Server).InitialCatalog);
        Assert.Equal(0, await CountAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Probe"));
    }

    [Fact]
    public void TwoRuns_GetDifferentDatabases()
    {
        var first = NewDatabase(TemporaryTestDatabase.CreateEmpty);
        var second = NewDatabase(TemporaryTestDatabase.CreateEmpty);

        Assert.NotEqual(Catalog(first), Catalog(second));
    }

    [Fact]
    public async Task LeftoverWithoutConnections_IsDropped_ButOneInUseStays()
    {
        var abandoned = NewDatabase(TemporaryTestDatabase.CreateEmpty);
        var inUse = NewDatabase(TemporaryTestDatabase.CreateEmpty);
        _ = abandoned.ConnectionString;
        await using var open = new SqlConnection(inUse.ConnectionString);
        await open.OpenAsync();

        _ = NewDatabase(TemporaryTestDatabase.CreateEmpty, orphanAge: TimeSpan.Zero).ConnectionString;

        Assert.False(await ExistsAsync(Catalog(abandoned)));
        Assert.True(await ExistsAsync(Catalog(inUse)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("with-dash")]
    [InlineData("a'b")]
    public void Prefix_ThatIsNotPlain_IsRejected(string prefix) =>
        Assert.Throws<ArgumentException>(() => new TemporaryTestDatabase(prefix, () => Server, _ => { }));

    private static string Server => BuildingBlocksTestDatabase.ConnectionString;

    private TemporaryTestDatabase NewDatabase(Action<string> createSchema, TimeSpan? orphanAge = null) =>
        new(_prefix, () => Server, createSchema, orphanAge);

    private static string Catalog(TemporaryTestDatabase database) =>
        new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;

    private static async Task<bool> ExistsAsync(string name) =>
        await CountAsync(Server, $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{name}'") == 1;

    private static async Task<int> CountAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
