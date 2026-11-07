extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Connectors.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Connectors (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class ConnectorsIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "con_tint",
        () =>
            Environment.GetEnvironmentVariable("CONNECTORS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("d5a2f07b-0550-4e76-a692-df921f63f798")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set CONNECTORS_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new ConnectorsDbContext(
                new DbContextOptionsBuilder<ConnectorsDbContext>().UseSqlServer(connection).Options,
                new NoTenant()
            );
            db.Database.Migrate();
        }
    );

    public static string ConnectionString => Database.ConnectionString;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
