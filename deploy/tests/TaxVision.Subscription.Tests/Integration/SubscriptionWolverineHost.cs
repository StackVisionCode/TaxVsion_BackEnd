using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Common;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Host mínimo de Wolverine con la persistencia de Subscription: SubscriptionDbContext sobre la base local
/// (filas del tenant de la prueba) y las tablas de sobres en un esquema propio que se borra al terminar.
/// Mismas piezas que Program.cs (transacciones EF, AutoApplyTransactions, middleware de tenant), sin Rabbit.
/// </summary>
internal static class SubscriptionWolverineHost
{
    public static string NewSchema(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    public static Task<IHost> StartAsync(
        string schema,
        Action<WolverineOptions> configure,
        Action<DbContextOptionsBuilder>? configureDb = null
    ) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "subscription-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(SubscriptionIntegrationDb.Connection, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(100);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                AddSubscriptionPersistence(options.Services, configureDb);
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
                options.Policies.AutoApplyTransactions();
                options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

                configure(options);
            })
            .StartAsync();

    private static void AddSubscriptionPersistence(
        IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb
    )
    {
        services.AddBuildingBlocks();
        services.AddDbContext<SubscriptionDbContext>(db =>
        {
            db.UseSqlServer(SubscriptionIntegrationDb.Connection);
            configureDb?.Invoke(db);
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();
        services.AddScoped<ISubscriptionSeatRepository, SubscriptionSeatRepository>();
        services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
        services.AddScoped<IAddOnDefinitionRepository, AddOnDefinitionRepository>();
        services.AddScoped<ISeatPricingRepository, SeatPricingRepository>();
        services.AddScoped<ITenantEntitlementSnapshotRepository, TenantEntitlementSnapshotRepository>();
        services.AddScoped<ISubscriptionTenantSettingsRepository, SubscriptionTenantSettingsRepository>();
        services.AddSingleton<ICacheService, NoOpCacheService>();
    }

    public static async Task<int> CountRowsAsync(string schema, string table, string where = "1 = 1")
    {
        await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[{schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{schema}].[{table}] WHERE {where}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>Borra el esquema de prueba entero: primero las FK (wolverine_nodes tiene una), después lo demás.</summary>
    public static async Task DropSchemaAsync(string schema)
    {
        await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
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

    private sealed class NoOpCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
