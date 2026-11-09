extern alias BbTesting;

using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// Host mínimo de Wolverine con la persistencia de PaymentApp, sobre una base temporal (BB-10): una corrida abortada
/// no le deja pagos ni sobres a la flota, que cobra lo que encuentra. Mismas piezas que Program.cs (transacciones EF,
/// AutoApplyTransactions, middleware de tenant), sin Rabbit ni jobs.
/// </summary>
internal static class PaymentAppWolverineHost
{
    private static readonly TemporaryTestDatabase Database = new(
        "payapp_tint",
        () =>
            Environment.GetEnvironmentVariable("PAYMENTAPP_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-paymentapp001")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set PAYMENTAPP_DB_CONNECTION or the PaymentApp user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new PaymentAppDbContext(
                new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(connection).Options,
                new NoTenant()
            );
            db.Database.Migrate();
        }
    );

    public static string Connection => Database.ConnectionString;

    public static string NewSchema(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    public static Task<IHost> StartAsync(
        string schema,
        Action<WolverineOptions> configure,
        Action<DbContextOptionsBuilder>? configureDb = null
    ) =>
        Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "paymentapp-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(Connection, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(100);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                options.Services.AddBuildingBlocks();
                options.Services.AddDbContext<PaymentAppDbContext>(db =>
                {
                    db.UseSqlServer(Connection);
                    configureDb?.Invoke(db);
                });
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentAppDbContext>());
                options.Services.AddScoped<ISaaSPaymentRepository, SaaSPaymentRepository>();
                options.Services.AddScoped<ITenantProviderCustomerRepository, TenantProviderCustomerRepository>();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PaymentAppDbContext>();
                options.Policies.AutoApplyTransactions();
                options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

                configure(options);
            })
            .StartAsync();

    public static async Task<int> CountRowsAsync(string schema, string table, string where = "1 = 1")
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[{schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{schema}].[{table}] WHERE {where}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task ExecuteAsync(string sql, Guid tenantId)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@tenant", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Borra el esquema de prueba entero: primero las FK (wolverine_nodes tiene una), después lo demás.</summary>
    public static async Task DropSchemaAsync(string schema)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.objects o JOIN sys.schemas s ON o.schema_id = s.schema_id
             WHERE s.name = N'{schema}' AND o.type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{schema}') + N';';
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
