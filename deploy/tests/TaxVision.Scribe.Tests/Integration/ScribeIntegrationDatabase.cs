extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Scribe.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Scribe (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class ScribeIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "scr_tint",
        () =>
            Environment.GetEnvironmentVariable("SCRIBE_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("44915234-5957-4298-a439-621831933142")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set SCRIBE_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new ScribeDbContext(
                new DbContextOptionsBuilder<ScribeDbContext>().UseSqlServer(connection).Options,
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
