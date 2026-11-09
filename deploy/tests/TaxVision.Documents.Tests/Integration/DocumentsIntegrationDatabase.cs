extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Documents.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Documents.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Documents (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class DocumentsIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "doc_tint",
        () =>
            Environment.GetEnvironmentVariable("DOCUMENTS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-documents00001")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set DOCUMENTS_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new DocumentsDbContext(
                new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(connection).Options,
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
