extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Auth.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Auth (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class AuthIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "auth_tint",
        () =>
            Environment.GetEnvironmentVariable("AUTH_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set AUTH_DB_CONNECTION or the Api user-secrets to run this test."),
        connection =>
        {
            using var db = new AuthDbContext(
                new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connection).Options,
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
