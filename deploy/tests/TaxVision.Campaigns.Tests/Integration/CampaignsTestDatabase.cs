extern alias BbTesting;
using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;

using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using TaxVision.Campaigns.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Campaigns (BB-10): la flota no la lee, así que una corrida abortada no le deja filas
/// ni sobres. Una por proceso (ver <see cref="TemporaryTestDatabase"/>). Cada prueba usa un tenant propio. La base local
/// (<c>CAMPAIGNS_DB_CONNECTION</c> o los user-secrets de la Api) solo ubica el servidor.
/// </summary>
internal static class CampaignsTestDatabase
{
    public static readonly string ServerConnectionString =
        Environment.GetEnvironmentVariable("CAMPAIGNS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("b8e4f2a1-6c3d-4a9e-9b17-2f5c8d0a1e34")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set CAMPAIGNS_DB_CONNECTION or the Campaigns user-secrets to run this test."
        );

    private static readonly TemporaryTestDatabase Temporary = new(
        "cmp_tint",
        () => ServerConnectionString,
        connection =>
        {
            using var db = new CampaignsDbContext(
                new DbContextOptionsBuilder<CampaignsDbContext>().UseSqlServer(connection).Options,
                new FixedTenant(Guid.Empty)
            );
            db.Database.EnsureCreated();
        }
    );

    public static string ConnectionString => Temporary.ConnectionString;

    public static CampaignsDbContext NewContext(Guid tenantId, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<CampaignsDbContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            new FixedTenant(tenantId)
        );

    public static async Task DeleteTenantAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM CampaignRecipients WHERE TenantId = @t; "
            + "DELETE FROM CampaignRuns WHERE TenantId = @t; "
            + "DELETE FROM CampaignSchedules WHERE TenantId = @t; "
            + "DELETE FROM ContactListMembers WHERE TenantId = @t; "
            + "DELETE FROM ContactLists WHERE TenantId = @t; "
            + "DELETE FROM Contacts WHERE TenantId = @t; "
            + "DELETE FROM Campaigns WHERE TenantId = @t;";
        command.Parameters.AddWithValue("@t", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<int> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    internal sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
