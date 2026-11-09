using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Host mínimo de Wolverine sobre la base local: transacciones EF como en Program.cs, sobres en un
/// esquema propio que se borra al terminar y transportes externos apagados.</summary>
internal static class WalletIntegrationHost
{
    private const string SchemaPrefix = "wallet_tint_";

    public static string NewSchema() => $"{SchemaPrefix}{Guid.NewGuid():N}"[..20];

    public static Task<IHost> StartAsync(
        string schema,
        Guid tenantId,
        Type[] handlers,
        Action<IServiceCollection>? services = null
    ) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddDbContext<WalletDbContext>(o => o.UseSqlServer(WalletTestDatabase.ConnectionString));
                s.AddScoped<IUnitOfWork>(p => p.GetRequiredService<WalletDbContext>());
                s.AddSingleton<ITenantContext>(new WalletTestDatabase.FixedTenant(tenantId));
                s.AddScoped<IWalletRepository, WalletRepository>();
                s.AddScoped<IReservationRepository, ReservationRepository>();
                // Catálogo falso: no se toca el catálogo global de precios de la base local.
                s.AddSingleton<IPriceBookRepository>(new FakePriceBookRepository());
                s.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                services?.Invoke(s);
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                foreach (var handler in handlers)
                    options.Discovery.IncludeType(handler);
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(WalletTestDatabase.ConnectionString, schema);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, WalletDbContext>();
                options.Policies.AutoApplyTransactions();
                options.StubAllExternalTransports();
            })
            .StartAsync();

    /// <summary>Borra todo lo que Wolverine creó en el esquema de prueba y el esquema mismo.</summary>
    public static async Task DropSchemaAsync(string schema)
    {
        if (!schema.StartsWith(SchemaPrefix, StringComparison.Ordinal))
            return;

        const string sql = """
            DECLARE @sql nvarchar(max) = N'';
            DECLARE @sid int = SCHEMA_ID(@schema);
            IF @sid IS NULL RETURN;
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
                + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' FROM sys.foreign_keys WHERE schema_id = @sid;
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.procedures WHERE schema_id = @sid;
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.objects WHERE schema_id = @sid AND type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.tables WHERE schema_id = @sid;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.sequences WHERE schema_id = @sid;
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.types WHERE is_user_defined = 1 AND schema_id = @sid;
            SET @sql += N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';
            EXEC sp_executesql @sql;
            """;

        await using var connection = new SqlConnection(WalletTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        await command.ExecuteNonQueryAsync();
    }
}
