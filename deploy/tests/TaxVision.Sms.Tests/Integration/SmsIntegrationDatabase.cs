extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Sms.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Sms.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Sms (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class SmsIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "sms_tint",
        () =>
            Environment.GetEnvironmentVariable("SMS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("b8e2f1a4-7c3d-4e9a-9f21-3c5d8a1b6e42")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set SMS_DB_CONNECTION or the Api user-secrets to run this test."),
        connection =>
        {
            using var db = new SmsDbContext(
                new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(connection).Options,
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
