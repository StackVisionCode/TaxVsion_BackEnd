extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Tenant.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Tenant (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class TenantIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "ten_tint",
        () =>
            new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .AddEnvironmentVariables()
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured for Tenant.Api."),
        connection =>
        {
            using var db = new TenantDbContext(
                new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(connection).Options,
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
