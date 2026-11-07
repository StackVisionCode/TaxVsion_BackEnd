using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-14: lo que una corrida abortada le deja al Billing en marcha. La base local solo se lee, con la misma
/// consulta del job de reconciliación: lo que esa consulta devuelve, el job lo publica.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class AbortedRunIntegrationTests(BillingTestDatabase database)
{
    private const string LeftoverDatabase = "billing_bil14_aborted_it";
    private static readonly DateTime LongAgo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AbortedRun_LeavesNoCandidateForTheRunningBilling()
    {
        var run = new BillingDurableLocalQueueIntegrationTests(database);
        try
        {
            // Sembró y el proceso se cae aquí: nunca llega a su DisposeAsync.
            await run.InitializeAsync();

            await using var local = new BillingDbContext(
                new DbContextOptionsBuilder<BillingDbContext>()
                    .UseSqlServer(BillingTestDatabase.LocalConnectionString)
                    .Options,
                new NoTenant()
            );
            var candidates = await new InvoiceSyncCandidateQuery(local).ListAsync(
                LongAgo,
                DateTime.UtcNow,
                take: 10_000
            );

            Assert.DoesNotContain(candidates, invoice => invoice.TenantId == run.TenantId);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [Fact]
    public async Task NextRun_DropsWhatTheAbortedRunLeft()
    {
        var aborted = new BillingTestDatabase(LeftoverDatabase);
        await aborted.InitializeAsync();
        await ExecuteAsync(aborted.ConnectionString, "CREATE TABLE dbo.AbortedRunLeftover (Id int NOT NULL);");

        var next = new BillingTestDatabase(LeftoverDatabase);
        try
        {
            await next.InitializeAsync();

            Assert.Equal(
                0,
                await ExecuteAsync(
                    next.ConnectionString,
                    "SELECT COUNT(*) FROM sys.tables WHERE name = 'AbortedRunLeftover';"
                )
            );
        }
        finally
        {
            await next.DisposeAsync();
        }
    }

    private static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
