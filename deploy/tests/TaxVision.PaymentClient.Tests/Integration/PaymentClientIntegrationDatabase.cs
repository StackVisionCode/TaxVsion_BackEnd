extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de PaymentClient (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class PaymentClientIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "payc_tint",
        () =>
            Environment.GetEnvironmentVariable("PAYMENTCLIENT_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("7c2a4f19-3e6b-4d02-8a91-paymentclient1")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set PAYMENTCLIENT_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new PaymentClientDbContext(
                new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(connection).Options,
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
