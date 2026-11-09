using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Growth.Infrastructure.Persistence;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Base temporal propia con el modelo de Growth: ni filas ni sobres tocan los esquemas reales, aunque la prueba se
/// aborte (el barrido de reservas de la flota nunca las ve). Las sobras de una corrida abortada se borran al
/// arrancar la siguiente.
/// </summary>
internal sealed class GrowthTemporaryDatabase
{
    private const string Prefix = "grwbb_";

    private static readonly Lazy<string> BaseConnection = new(() =>
        Environment.GetEnvironmentVariable("GROWTH_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("799f9d80-8d1c-47d2-925d-taxvision-growth")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set GROWTH_DB_CONNECTION or the Growth user-secrets to run this test.")
    );

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(BaseConnection.Value)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;

    public async Task CreateAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DropAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
    }

    public GrowthDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<GrowthDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    public async Task<int> CountEnvelopesAsync(string table, string messageType)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[{GrowthSchemas.Integration}].[{table}]') IS NULL SELECT 0 ELSE "
            + $"SELECT COUNT(*) FROM [{GrowthSchemas.Integration}].[{table}] WHERE message_type = @type";
        command.Parameters.AddWithValue("@type", messageType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Solo las de más de una hora: las recientes pueden ser de otra clase de pruebas corriendo ahora.
    private static async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(BaseConnection.Value)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();

        var leftovers = new List<string>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText =
                $"SELECT name FROM sys.databases WHERE name LIKE '{Prefix.Replace("_", "[_]")}%' "
                + "AND create_date < DATEADD(HOUR, -1, GETDATE())";
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                leftovers.Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
