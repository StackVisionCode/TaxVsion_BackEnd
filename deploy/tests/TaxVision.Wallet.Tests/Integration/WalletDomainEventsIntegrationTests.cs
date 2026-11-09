using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Application.Wallet.DomainEvents;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>
/// WAL-BB1 contra el SQL Server local: UseTaxVisionDomainEvents() con el WalletDbContext real. Host mínimo de
/// Wolverine, sobres en un esquema propio que se borra al terminar y avisos a un puerto TCP cerrado (quedan en
/// wolverine_outgoing_envelopes y se pueden contar).
/// </summary>
public sealed class WalletDomainEventsIntegrationTests : IAsyncLifetime
{
    private const string ReferenceType = "campaign-run";
    private readonly string _schema = $"walbb1_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _referenceId = Guid.NewGuid();
    private readonly FailAfterExpireInterceptor _interceptor = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            var wallet = WalletAggregate.Create(_tenantId).Value;
            db.LedgerEntries.Add(wallet.Credit(Usd(10_000_000), $"topup:{Guid.NewGuid():N}").Value);
            db.LedgerEntries.Add(
                wallet.Reserve(Usd(500_000), $"wallet-reserve:{ReferenceType}:{_referenceId:N}").Value
            );
            db.Wallets.Add(wallet);
            db.Reservations.Add(ReservationBuilder.Open(_tenantId, ReferenceType, _referenceId, 500_000, 5));
            await db.SaveChangesAsync();
        }
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            await db.Reservations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task ExpireReservation_EnvelopeStoredWithRow()
    {
        var result = await ExpireAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ReservationStatus.Expired, await StatusAsync());
        Assert.Equal(1, await CountReleasesAsync());
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "WalletBalanceChangedDomainEvent"));

        // El handler del evento de dominio devuelve un solo aviso de saldo.
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(WalletBalanceChangedIntegrationEvent)) == 1
            )
        );
    }

    [Fact]
    public async Task ExpireReservation_FailureAfterSave_LeavesNoRowNorEnvelope()
    {
        _interceptor.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(ExpireAsync);

        Assert.Equal(ReservationStatus.Open, await StatusAsync());
        Assert.Equal(0, await CountReleasesAsync());
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "WalletBalanceChangedDomainEvent"));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(WalletBalanceChangedIntegrationEvent))
        );
    }

    private Task<Result<ReservationExpiry>> ExpireAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<ReservationExpiry>>(
                new ExpireReservationCommand(_tenantId, ReferenceType, _referenceId)
            );

    private async Task<ReservationStatus> StatusAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        return await db
            .Reservations.IgnoreQueryFilters()
            .Where(r => r.TenantId == _tenantId)
            .Select(r => r.Status)
            .SingleAsync();
    }

    private async Task<int> CountReleasesAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        return await db
            .LedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == _tenantId && e.Movement == MovementType.Release);
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "wal-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ExpireReservationHandler))
                    .IncludeType(typeof(WalletBalanceChangedDomainEventHandler));
                options.PersistMessagesWithSqlServer(WalletTestDatabase.ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<WalletDbContext>(db =>
                    db.UseSqlServer(WalletTestDatabase.ConnectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<WalletDbContext>());
                options.Services.AddSingleton<ITenantContext>(new WalletTestDatabase.FixedTenant(_tenantId));
                options.Services.AddScoped<IWalletRepository, WalletRepository>();
                options.Services.AddScoped<IReservationRepository, ReservationRepository>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, WalletDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();

                options.PublishMessage<WalletBalanceChangedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
        );

    private static async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(WalletTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(WalletTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Borra las tablas de Wolverine del esquema de prueba (primero las FK) y el esquema.
    private Task DropSchemaAsync() =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{_schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{_schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{_schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Falla después de guardar el vencimiento y antes de confirmar: la transacción de Wolverine deshace fila y sobre.
    private sealed class FailAfterExpireInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                Fail
                && eventData
                    .Context!.ChangeTracker.Entries<WalletReservation>()
                    .Any(e => e.Entity.Status == ReservationStatus.Expired);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
