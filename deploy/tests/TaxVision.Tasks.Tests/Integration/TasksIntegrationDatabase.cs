extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Tasks.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Tasks (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class TasksIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "tsk_tint",
        () =>
            Environment.GetEnvironmentVariable("TASKS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set TASKS_DB_CONNECTION or the Api user-secrets to run this test."),
        connection =>
        {
            using var db = new TasksDbContext(
                new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(connection).Options,
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
