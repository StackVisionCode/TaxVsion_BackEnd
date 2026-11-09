using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>Base temporal propia: se crea con el modelo actual y se borra al terminar, sin tocar la de la flota.</summary>
internal sealed class NotificationTempDatabase(string prefix) : IAsyncDisposable
{
    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
                ?? new ConfigurationBuilder()
                    .AddUserSecrets("f7c2d8e1-3b5a-4c97-8d1e-notification1")
                    .Build()
                    .GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Set NOTIFICATION_DB_CONNECTION or the Notification user-secrets."
                )
        )
        {
            InitialCatalog = $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 12)],
        }.ConnectionString;

    public NotificationDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenant()
        );

    public async Task CreateAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();

        var leftovers = new List<string>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText =
                $"SELECT name FROM sys.databases WHERE name LIKE '{prefix.Replace("_", "[_]")}%' "
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

    public sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
