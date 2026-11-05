using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>
/// Contra el SQL Server local, con el middleware transaccional de Wolverine como en Program.cs: un
/// ReserveFunds que devuelve Failure no deja HeldMicros movido. El outbox va a un esquema propio que se
/// borra al terminar, para no cruzarse con los sobres del servicio real.
/// </summary>
public sealed class ReserveFundsAtomicityIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = $"wallet_tint_{Guid.NewGuid():N}"[..20];
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            var wallet = WalletAggregate.Create(_tenantId).Value;
            db.LedgerEntries.Add(wallet.Credit(10_000_000, $"topup:{Guid.NewGuid():N}").Value);
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
        }

        var connectionString = WalletTestDatabase.ConnectionString;
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<WalletDbContext>(o => o.UseSqlServer(connectionString));
                services.AddScoped<IUnitOfWork>(p => p.GetRequiredService<WalletDbContext>());
                services.AddSingleton<ITenantContext>(new WalletTestDatabase.FixedTenant(_tenantId));
                services.AddScoped<IWalletRepository, WalletRepository>();
                services.AddScoped<IReservationRepository, ReservationRepository>();
                // Catálogo falso: no se toca el catálogo global de precios de la base local.
                services.AddSingleton<IPriceBookRepository>(new FakePriceBookRepository());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ReserveFundsHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(connectionString, _schema);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, WalletDbContext>();
                options.Policies.AutoApplyTransactions();
                options.StubAllExternalTransports();
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Reservations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
        }

        await DropTestSchemaAsync();
    }

    [Fact]
    public async Task FailureResult_DoesNotPersistHeldChange()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<Result<ReservationView>>(
            new ReserveFundsCommand(_tenantId, "", Guid.NewGuid(), new PerChannelUnits(Sms: 1))
        );

        Assert.True(result.IsFailure);
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.TenantId == _tenantId);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.False(
            await db.LedgerEntries.AnyAsync(e => e.TenantId == _tenantId && e.DeltaHeldMicros != 0),
            "No debe quedar asiento de reserva."
        );
    }

    // Borra todo lo que Wolverine creó en el esquema de prueba y el esquema mismo.
    private async Task DropTestSchemaAsync()
    {
        if (!_schema.StartsWith("wallet_tint_", StringComparison.Ordinal))
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
        command.Parameters.AddWithValue("@schema", _schema);
        await command.ExecuteNonQueryAsync();
    }
}
