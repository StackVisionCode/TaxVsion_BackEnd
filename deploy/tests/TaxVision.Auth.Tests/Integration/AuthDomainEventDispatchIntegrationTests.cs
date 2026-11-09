using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.SlotGating.Consumers;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Domain.Users.Events;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// AUTH-BB1 contra el SQL Server local: host mínimo de Wolverine con el AuthDbContext real (tablas de la base de
/// desarrollo) y los sobres en un esquema propio que se borra al terminar, para no cruzarse con el Auth de la
/// flota. Lo que sale hacia otros servicios va a un puerto TCP cerrado y se queda en wolverine_outgoing_envelopes.
/// </summary>
public sealed class AuthDomainEventDispatchIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString = AuthIntegrationDatabase.ConnectionString;

    private readonly string _schema = $"authbb1_{Guid.NewGuid():N}"[..16];
    private readonly Guid _tenantId = Guid.NewGuid();
    private IHost _host = null!;
    private User _employee = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        _employee = User.Register(
            _tenantId,
            "Slot",
            "Probe",
            $"{Guid.NewGuid():N}@example.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        await using (var db = NewContext())
        {
            db.Users.Add(_employee);
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
        await using (var db = NewContext())
            await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == _tenantId).ExecuteDeleteAsync();
        await DropSchemaAsync();
    }

    [Fact]
    public async Task UserSlotReleased_IsStoredInSameTransactionAsUser()
    {
        await Bus.InvokeAsync(new ReleaseUserSlotProbe(_employee.Id, FailAfterSave: false));

        Assert.False(await IsActiveAsync());
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", nameof(UserSlotPriorityReleased)));
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RecomputeTenantSeatSlotsCommand)) == 1
            ),
            "El recálculo de asientos que pide el evento de dominio no salió."
        );
    }

    [Fact]
    public async Task FailureAfterSave_LeavesNoRowAndNoEnvelope()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bus.InvokeAsync(new ReleaseUserSlotProbe(_employee.Id, FailAfterSave: true))
        );

        Assert.True(await IsActiveAsync());
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", nameof(UserSlotPriorityReleased)));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RecomputeTenantSeatSlotsCommand)));
    }

    // Un aviso por acción, medido en el cable: el override sale una sola vez hacia los demás servicios.
    [Fact]
    public async Task SlotOverride_EmitsExactlyOneIntegrationEvent()
    {
        await Bus.InvokeAsync(new OverrideUserSlotProbe(_employee.Id));

        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(UserSlotPriorityOverriddenIntegrationEvent))
                >= 1
            ),
            "El override no salió hacia los demás servicios."
        );
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(
            1,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(UserSlotPriorityOverriddenIntegrationEvent))
        );
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", nameof(UserSlotPriorityOverridden)));
    }

    private IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "auth-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(SlotProbeHandler))
                    .IncludeType(typeof(UserSlotPriorityDomainEventHandler));
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<AuthDbContext>(db => db.UseSqlServer(ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
                options.Services.AddSingleton<ITenantContext>(new NoTenantContext());
                options.Services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options.PublishMessage<RecomputeTenantSeatSlotsCommand>().ToPort(closedPort).UseDurableOutbox();
                options
                    .PublishMessage<UserSlotPriorityOverriddenIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            })
            .StartAsync();
    }

    private async Task<bool> IsActiveAsync()
    {
        await using var db = NewContext();
        return await db
            .Users.IgnoreQueryFilters()
            .Where(u => u.Id == _employee.Id)
            .Select(u => u.IsActive)
            .SingleAsync();
    }

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
        );

    private static async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Borra las tablas de Wolverine del esquema de prueba y el esquema (primero las FK).
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

    private AuthDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(ConnectionString).Options, new NoTenantContext());

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

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

public sealed record ReleaseUserSlotProbe(Guid UserId, bool FailAfterSave);

public sealed record OverrideUserSlotProbe(Guid UserId);

/// <summary>Handlers de prueba que mutan un User dentro del pipeline de Wolverine, como un comando real.</summary>
public static class SlotProbeHandler
{
    public static async Task Handle(ReleaseUserSlotProbe command, AuthDbContext db, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == command.UserId, ct);
        user.Deactivate(Guid.NewGuid(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        if (command.FailAfterSave)
            throw new InvalidOperationException("Fallo forzado después de guardar.");
    }

    public static async Task Handle(OverrideUserSlotProbe command, AuthDbContext db, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == command.UserId, ct);
        user.OverrideSlotPriority(DateTime.UtcNow.AddDays(-30), Guid.NewGuid(), "probe");
        await db.SaveChangesAsync(ct);
    }
}
