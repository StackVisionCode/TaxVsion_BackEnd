using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// Base temporal propia con el modelo de Tasks: la flota nunca ve estas filas ni estos sobres. Las sobras de una
/// corrida abortada se borran al arrancar la siguiente.
/// </summary>
internal sealed class TasksTemporaryDatabase
{
    private const string Prefix = "tskbb_";

    private static readonly string BaseConnectionString =
        Environment.GetEnvironmentVariable("TASKS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set TASKS_DB_CONNECTION or the Tasks Api user-secrets.");

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(BaseConnectionString)
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

    public TasksDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    // Solo las de más de una hora: las recientes pueden ser de otra clase de pruebas corriendo ahora.
    private static async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(BaseConnectionString) { InitialCatalog = "master" }.ConnectionString;
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
