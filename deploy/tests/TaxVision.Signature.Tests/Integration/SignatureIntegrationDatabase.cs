extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Signature.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Signature (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class SignatureIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "sig_tint",
        () =>
            Environment.GetEnvironmentVariable("SIGNATURE_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("4a1e9c8d-2f7b-4e15-9c3d-signature10001")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set SIGNATURE_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new SignatureDbContext(
                new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(connection).Options,
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
