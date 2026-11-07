extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Calendar (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class CalendarIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "cal_tint",
        () =>
            Environment.GetEnvironmentVariable("CALENDAR_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("b7f4a1d2-9c3e-4a58-8f21-6d0e5b3c74a9")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set CALENDAR_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new CalendarDbContext(
                new DbContextOptionsBuilder<CalendarDbContext>().UseSqlServer(connection).Options,
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
