extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Reminder.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Reminder.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Reminder (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class ReminderIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "rem_tint",
        () =>
            Environment.GetEnvironmentVariable("REMINDER_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("7e9e9b63-4868-4cc3-8876-b96a161ee297")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set REMINDER_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new ReminderDbContext(
                new DbContextOptionsBuilder<ReminderDbContext>().UseSqlServer(connection).Options,
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
