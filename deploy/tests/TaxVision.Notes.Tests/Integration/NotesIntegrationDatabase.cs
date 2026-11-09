extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Notes.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Notes (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class NotesIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "not_tint",
        () =>
            Environment.GetEnvironmentVariable("NOTES_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("d61a76cb-cc40-479a-a7a3-a0f76548ef8c")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set NOTES_DB_CONNECTION or the Api user-secrets to run this test."),
        connection =>
        {
            using var db = new NotesDbContext(
                new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(connection).Options,
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
