extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Notification.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Notification (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class NotificationIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "ntf_tint",
        () =>
            Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("f7c2d8e1-3b5a-4c97-8d1e-notification1")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set NOTIFICATION_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new NotificationDbContext(
                new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(connection).Options,
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
