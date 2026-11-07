extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Wallet.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Wallet (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class WalletIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "wal_tint",
        () =>
            Environment.GetEnvironmentVariable("WALLET_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("6fb4d16c-9644-4b66-b322-179dcdf03dfc")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set WALLET_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new WalletDbContext(
                new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(connection).Options,
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
