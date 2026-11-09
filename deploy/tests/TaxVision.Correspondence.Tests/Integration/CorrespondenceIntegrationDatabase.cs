extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Correspondence.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Correspondence (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class CorrespondenceIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "cor_tint",
        () =>
            Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("236deafb-0353-4dac-8aed-b15fbb76ae20")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set CORRESPONDENCE_SQLSERVER_TESTS or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new CorrespondenceDbContext(
                new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(connection).Options,
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
