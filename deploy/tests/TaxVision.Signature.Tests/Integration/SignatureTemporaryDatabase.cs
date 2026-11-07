using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Base temporal propia con el modelo de Signature: la flota nunca ve estas solicitudes ni estos sobres. Las sobras
/// de una corrida abortada se borran al arrancar la siguiente.
/// </summary>
internal sealed class SignatureTemporaryDatabase
{
    private const string Prefix = "sigbb_";

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(SignatureTestDatabase.ConnectionString)
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

    public SignatureDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    // Solo las de más de una hora: las recientes pueden ser de otra clase de pruebas corriendo ahora.
    private static async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(SignatureTestDatabase.ConnectionString)
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
