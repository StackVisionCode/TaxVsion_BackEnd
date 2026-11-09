using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// BB-1 contra el SQL Server real: <c>UseTaxVisionDomainEvents()</c> saca los eventos de las dos raíces
/// (<see cref="AggregateRoot"/> y <see cref="TenantlessAggregateRoot"/>) y los deja en la cola local durable dentro
/// de la misma transacción que la fila. El evento de integración que devuelve el handler sale por el outbox
/// hacia un puerto TCP cerrado, así que se queda en <c>wolverine_outgoing_envelopes</c>.
/// El handler del evento de dominio espera en <see cref="ProbeGate"/> para poder ver el sobre antes de que
/// Wolverine lo procese y lo borre. Esquema propio por prueba, borrado al terminar.
/// </summary>
public sealed class DomainEventDispatchIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = BuildingBlocksTestDatabase.NewSchema("bb1test");
    private readonly ProbeGate _gate = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await BuildingBlocksTestDatabase.ExecuteAsync(
            $"EXEC('CREATE SCHEMA [{_schema}]'); "
                + $"CREATE TABLE [{_schema}].[Probes] (Id uniqueidentifier NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL, Name nvarchar(100) NOT NULL); "
                + $"CREATE TABLE [{_schema}].[TenantlessProbes] (Id uniqueidentifier NOT NULL PRIMARY KEY, Name nvarchar(100) NOT NULL);"
        );
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        _gate.Open();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await BuildingBlocksTestDatabase.DropSchemaAsync(_schema);
    }

    [Fact]
    public async Task EventRaised_IsStoredInSameTransaction()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();

        // Falla después de guardar: ni la fila ni el sobre.
        var failed = Guid.NewGuid();
        await Assert.ThrowsAsync<ForcedFailureException>(() => bus.InvokeAsync(new CreateProbe(failed, FailAfterSave: true)));
        Assert.Equal(0, await CountProbesAsync(failed));
        Assert.Equal(0, await CountIncomingAsync(nameof(ProbeCreatedDomainEvent)));

        // Camino feliz: la fila y el sobre del evento de dominio, juntos (el handler espera en la compuerta).
        var created = Guid.NewGuid();
        await bus.InvokeAsync(new CreateProbe(created, FailAfterSave: false));
        Assert.Equal(1, await CountProbesAsync(created));
        Assert.Equal(1, await CountIncomingAsync(nameof(ProbeCreatedDomainEvent)));
    }

    [Fact]
    public async Task HandlerReturnsIntegrationEvent_IsInOutbox()
    {
        _gate.Open();
        await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new CreateProbe(Guid.NewGuid(), FailAfterSave: false));

        Assert.True(
            await WaitUntilAsync(async () => await CountOutgoingAsync(nameof(ProbeCreatedIntegrationEvent)) == 1),
            "El evento de integración devuelto por el handler del evento de dominio no llegó a wolverine_outgoing_envelopes."
        );
    }

    [Fact]
    public async Task TenantlessAggregate_EventIsStoredAndDispatched()
    {
        await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new CreateTenantlessProbe(Guid.NewGuid()));
        Assert.Equal(1, await CountIncomingAsync(nameof(TenantlessProbeCreatedDomainEvent)));

        _gate.Open();
        Assert.True(
            await WaitUntilAsync(async () => await CountOutgoingAsync(nameof(TenantlessProbeCreatedIntegrationEvent)) == 1),
            "El evento de la raíz sin TenantId no se despachó."
        );
    }

    [Fact]
    public async Task EventRaised_DomainHandlerSeesCorrelationIdOfCommand()
    {
        _gate.Open();
        await using var scope = _host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageContext>();
        bus.CorrelationId = "bb9-command";

        await bus.InvokeAsync(new CreateProbe(Guid.NewGuid(), FailAfterSave: false));

        Assert.Equal("bb9-command", await _gate.SeenCorrelationId.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private async Task<IHost> StartHostAsync()
    {
        var connection = BuildingBlocksTestDatabase.ConnectionString;
        var schema = _schema;
        var closedPort = FreeTcpPort();

        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CreateProbeHandler))
                    .IncludeType(typeof(CreateTenantlessProbeHandler))
                    .IncludeType(typeof(ProbeCreatedDomainEventHandler))
                    .IncludeType(typeof(TenantlessProbeCreatedDomainEventHandler));
                options.PersistMessagesWithSqlServer(connection, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                // Como en los servicios: AddDbContext normal. Con AddDbContextWithWolverineIntegration (6.14) el
                // evento raspado no queda en el inbox durable.
                options.Services.AddDbContext<ProbeDbContext>(db =>
                    db.UseSqlServer(connection).ReplaceService<IModelCacheKeyFactory, ProbeModelCacheKeyFactory>()
                );
                options.Services.AddSingleton(new ProbeSchema(schema));
                options.Services.AddSingleton(_gate);
                options.UseEntityFrameworkCoreTransactions();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();

                // Destino externo que nunca contesta: lo enviado se queda en el outbox.
                options.PublishMessage<ProbeCreatedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<TenantlessProbeCreatedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<int> CountProbesAsync(Guid id) => BuildingBlocksTestDatabase.CountRowsAsync(_schema, "Probes", $"Id = '{id}'");

    private Task<int> CountIncomingAsync(string messageType) =>
        BuildingBlocksTestDatabase.CountRowsAsync(_schema, "wolverine_incoming_envelopes", $"message_type LIKE '%{messageType}%'");

    private Task<int> CountOutgoingAsync(string messageType) =>
        BuildingBlocksTestDatabase.CountRowsAsync(_schema, "wolverine_outgoing_envelopes", $"message_type LIKE '%{messageType}%'");

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
}

public sealed record CreateProbe(Guid Id, bool FailAfterSave);

public sealed record CreateTenantlessProbe(Guid Id);

public sealed record ProbeCreatedDomainEvent(Guid ProbeId) : IDomainEvent;

public sealed record TenantlessProbeCreatedDomainEvent(Guid ProbeId) : IDomainEvent;

public sealed record ProbeCreatedIntegrationEvent(Guid ProbeId);

public sealed record TenantlessProbeCreatedIntegrationEvent(Guid ProbeId);

public sealed class ForcedFailureException() : Exception("Fallo forzado después de guardar.");

public sealed class Probe : AggregateRoot
{
    public string Name { get; private set; } = "";

    public static Probe Create(Guid id)
    {
        var probe = new Probe { Id = id, Name = "probe" };
        probe.SetTenant(Guid.NewGuid());
        probe.AddDomainEvent(new ProbeCreatedDomainEvent(id));
        return probe;
    }
}

public sealed class TenantlessProbe : TenantlessAggregateRoot
{
    public string Name { get; private set; } = "";

    public static TenantlessProbe Create(Guid id)
    {
        var probe = new TenantlessProbe { Id = id, Name = "tenant" };
        probe.AddDomainEvent(new TenantlessProbeCreatedDomainEvent(id));
        return probe;
    }
}

public sealed record ProbeSchema(string Name);

/// <summary>Retiene los handlers de eventos de dominio hasta que la prueba la abre.</summary>
public sealed class ProbeGate
{
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<string> _seenCorrelationId = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string> SeenCorrelationId => _seenCorrelationId.Task;

    public void RecordCorrelationId(string correlationId) => _seenCorrelationId.TrySetResult(correlationId);

    public Task WaitAsync() => _opened.Task;

    public void Open() => _opened.TrySetResult();
}

public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ProbeSchema schema) : DbContext(options)
{
    public string SchemaName => schema.Name;

    public DbSet<Probe> Probes => Set<Probe>();
    public DbSet<TenantlessProbe> TenantlessProbes => Set<TenantlessProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Probe>(b =>
        {
            b.ToTable("Probes", schema.Name);
            b.Ignore(p => p.DomainEvents);
        });
        modelBuilder.Entity<TenantlessProbe>(b =>
        {
            b.ToTable("TenantlessProbes", schema.Name);
            b.Ignore(p => p.DomainEvents);
        });
    }
}

/// <summary>Un modelo por esquema: cada prueba usa el suyo y EF cachea el modelo por tipo.</summary>
public sealed class ProbeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), (context as ProbeDbContext)?.SchemaName, designTime);
}

public static class CreateProbeHandler
{
    public static async Task Handle(CreateProbe command, ProbeDbContext db, CancellationToken ct)
    {
        db.Probes.Add(Probe.Create(command.Id));
        await db.SaveChangesAsync(ct);
        if (command.FailAfterSave)
            throw new ForcedFailureException();
    }
}

public static class CreateTenantlessProbeHandler
{
    public static void Handle(CreateTenantlessProbe command, ProbeDbContext db) =>
        db.TenantlessProbes.Add(TenantlessProbe.Create(command.Id));
}

public static class ProbeCreatedDomainEventHandler
{
    public static async Task<ProbeCreatedIntegrationEvent> Handle(
        ProbeCreatedDomainEvent domainEvent,
        ProbeGate gate,
        ICorrelationContext correlation
    )
    {
        await gate.WaitAsync();
        gate.RecordCorrelationId(correlation.CorrelationId);
        return new(domainEvent.ProbeId);
    }
}

public static class TenantlessProbeCreatedDomainEventHandler
{
    public static async Task<TenantlessProbeCreatedIntegrationEvent> Handle(
        TenantlessProbeCreatedDomainEvent domainEvent,
        ProbeGate gate
    )
    {
        await gate.WaitAsync();
        return new(domainEvent.ProbeId);
    }
}
