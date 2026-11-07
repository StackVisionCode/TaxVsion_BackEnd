using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Base temporal propia con el modelo de Customer: el servicio local nunca ve estas filas ni estos sobres,
/// aunque la prueba se aborte. Las sobras de una corrida abortada se borran al arrancar la siguiente.
/// </summary>
public sealed class CustomerTemporaryDatabase : IAsyncLifetime
{
    private const string Prefix = "cusint_";

    private static readonly Lazy<string> BaseConnection = new(() =>
        Environment.GetEnvironmentVariable("CUSTOMER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("8c4e7963-1ddb-441e-af2d-755d58fb86a3")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set CUSTOMER_DB_CONNECTION or the Customer user-secrets.")
    );

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(BaseConnection.Value)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
    }

    public CustomerDbContext NewContext(Guid tenantId, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            new FixedTenant(tenantId)
        );

    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    public async Task<int> CountEnvelopesAsync(string table, string messageType) =>
        Convert.ToInt32(
            await ScalarAsync(
                $"IF OBJECT_ID('[wolverine].[{table}]') IS NULL SELECT 0 ELSE "
                    + $"SELECT COUNT(*) FROM [wolverine].[{table}] WHERE message_type LIKE @type",
                ("@type", $"%{messageType}%")
            )
        );

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

    internal sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
