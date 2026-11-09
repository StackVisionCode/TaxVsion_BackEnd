using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using TaxVision.Tenant.Infrastructure.Persistence;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Base temporal propia con el modelo de Tenant: el job del WISP de la flota nunca ve estas oficinas ni estos
/// sobres, aunque la prueba se aborte. Las sobras de una corrida abortada se borran al arrancar la siguiente.
/// </summary>
internal sealed class TenantTemporaryDatabase
{
    private const string Prefix = "tenbb_";

    private static readonly Lazy<string> BaseConnection = new(() =>
        new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured for Tenant.Api.")
    );

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(BaseConnection.Value)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;

    public async Task CreateAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DropAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    public TenantDbContext NewContext(params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            new NoTenantContext()
        );

    public async Task<int> CountEnvelopesAsync(string table, string messageType)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[{TenantWolverineHost.EnvelopeSchema}].[{table}]') IS NULL SELECT 0 ELSE "
            + $"SELECT COUNT(*) FROM [{TenantWolverineHost.EnvelopeSchema}].[{table}] WHERE message_type LIKE @type";
        command.Parameters.AddWithValue("@type", $"%{messageType}%");
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

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
