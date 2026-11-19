extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Mobile.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Mobile.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Mobile (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class MobileIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "mob_tint",
        () =>
            Environment.GetEnvironmentVariable("MOBILE_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("taxvision-mobile-development")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set MOBILE_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new MobileDbContext(
                new DbContextOptionsBuilder<MobileDbContext>().UseSqlServer(connection).Options,
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
