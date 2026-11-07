using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Billing.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-14: base temporal de las T-INT. El Billing en marcha no la lee, así que una corrida abortada no le deja
/// facturas ni sobres. Se borra al arrancar, por si otra corrida la dejó, y al terminar.
/// </summary>
public sealed class BillingTestDatabase : IAsyncLifetime
{
    public const string Collection = "billing-test-database";
    public const string DefaultName = "billing_tint_it";

    /// <summary>Base local de desarrollo: solo para ubicar el servidor y leer. Las pruebas no escriben en ella.</summary>
    public static string LocalConnectionString { get; } =
        Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-billing000001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set BILLING_DB_CONNECTION or the Billing user-secrets to run this test."
        );

    private readonly string _name;

    public BillingTestDatabase()
        : this(DefaultName) { }

    internal BillingTestDatabase(string name)
    {
        _name = name;
        ConnectionString = new SqlConnectionStringBuilder(LocalConnectionString)
        {
            InitialCatalog = name,
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public async Task InitializeAsync()
    {
        await DropAsync();
        await using var db = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenant()
        );
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await DropAsync();
    }

    private async Task DropAsync()
    {
        var master = new SqlConnectionStringBuilder(LocalConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_name}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}

/// <summary>Una sola migración para todas las T-INT que comparten la base; corren en serie.</summary>
[CollectionDefinition(BillingTestDatabase.Collection)]
public sealed class BillingTestDatabaseCollection : ICollectionFixture<BillingTestDatabase>;
