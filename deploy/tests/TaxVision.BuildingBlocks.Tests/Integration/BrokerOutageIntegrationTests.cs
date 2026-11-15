using System.Collections.Concurrent;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// Caída de RabbitMQ con la misma configuración que los servicios (<c>UseTaxVisionRabbitMq</c> + outbox e inbox
/// durables en SQL), contra el RabbitMQ y el SQL Server locales. El broker "se cae" con un proxy TCP, sin tocar el
/// contenedor. Exchange, cola y esquema son propios de cada prueba y se borran al terminar.
/// </summary>
public sealed class BrokerOutageIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = BuildingBlocksTestDatabase.NewSchema("bbrabbit");
    private readonly string _exchange = $"bb-chaos-{Guid.NewGuid():N}";
    private TcpChaosProxy _proxy = null!;

    public Task InitializeAsync()
    {
        var rabbit = LocalBrokers.RabbitUri;
        _proxy = new TcpChaosProxy(rabbit.Host, rabbit.IsDefaultPort || rabbit.Port < 0 ? 5672 : rabbit.Port);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _proxy.DisposeAsync();
        var factory = new ConnectionFactory { Uri = LocalBrokers.RabbitUri };
        await using (var connection = await factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            await channel.QueueDeleteAsync(_exchange);
            await channel.ExchangeDeleteAsync(_exchange);
        }
        await BuildingBlocksTestDatabase.DropSchemaAsync(_schema);
    }

    // Lo publicado con el broker caído queda en el outbox de SQL y llega una sola vez cuando vuelve.
    [Fact(Timeout = 240_000)]
    public async Task Messages_published_while_the_broker_is_down_arrive_once_after_it_returns()
    {
        var ledger = new ChaosLedger();
        using var host = BuildHost(ledger);
        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IMessageBus>();

        var before = await PublishAsync(bus, 5);
        await LocalBrokers.WaitUntilAsync(
            () => before.All(ledger.Received),
            TimeSpan.FromSeconds(30),
            "No llegaron los mensajes con el broker arriba."
        );

        _proxy.Cut();
        var during = await PublishAsync(bus, 10);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(during, ledger.Received);
        Assert.Equal(during.Count, await OutgoingAsync());

        _proxy.Restore();
        await LocalBrokers.WaitUntilAsync(
            () => during.All(ledger.Received),
            TimeSpan.FromSeconds(120),
            "Los mensajes del corte no llegaron después de volver el broker."
        );
        await LocalBrokers.WaitUntilAsync(
            async () => await OutgoingAsync() == 0,
            TimeSpan.FromSeconds(30),
            "El outbox no se vació."
        );

        // Una vuelta más para que un duplicado tardío alcance a aparecer.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.All(before.Concat(during), id => Assert.Equal(1, ledger.Times(id)));
        await host.StopAsync();
    }

    // Sin broker el host no arranca (Wolverine reintenta 20 veces y se rinde); con el broker de vuelta, el mismo
    // arranque funciona. Es el ciclo que cierra la política de reinicio de Docker.
    [Fact(Timeout = 300_000)]
    public async Task Without_the_broker_the_host_does_not_start_and_a_restart_works_once_it_returns()
    {
        _proxy.Cut();
        using (var failed = BuildHost(new ChaosLedger()))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => failed.StartAsync());
        }

        _proxy.Restore();
        var ledger = new ChaosLedger();
        using var host = BuildHost(ledger);
        await host.StartAsync();
        var ids = await PublishAsync(host.Services.GetRequiredService<IMessageBus>(), 3);
        await LocalBrokers.WaitUntilAsync(
            () => ids.All(ledger.Received),
            TimeSpan.FromSeconds(30),
            "El reinicio no quedó operativo."
        );
        await host.StopAsync();
    }

    private IHost BuildHost(ChaosLedger ledger) =>
        Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bb-broker-outage-test";
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ChaosPingHandler));
                options.Services.AddSingleton(ledger);
                options.PersistMessagesWithSqlServer(BuildingBlocksTestDatabase.ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                options.UseTaxVisionRabbitMq(LocalBrokers.ThroughProxy(LocalBrokers.RabbitUri, _proxy));
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options.PublishMessage<ChaosPing>().ToRabbitExchange(_exchange);
                options
                    .ListenToRabbitQueue(_exchange, queue => queue.BindExchange(_exchange, string.Empty))
                    .UseDurableInbox();
            })
            .Build();

    private static async Task<List<Guid>> PublishAsync(IMessageBus bus, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
            await bus.PublishAsync(new ChaosPing(id));
        return ids;
    }

    private Task<int> OutgoingAsync() =>
        BuildingBlocksTestDatabase.CountRowsAsync(_schema, "wolverine_outgoing_envelopes");
}

public sealed record ChaosPing(Guid Id);

public sealed class ChaosLedger
{
    private readonly ConcurrentDictionary<Guid, int> _received = new();

    public void Record(Guid id) => _received.AddOrUpdate(id, 1, (_, times) => times + 1);

    public bool Received(Guid id) => _received.ContainsKey(id);

    public int Times(Guid id) => _received.GetValueOrDefault(id);
}

public static class ChaosPingHandler
{
    public static void Handle(ChaosPing ping, ChaosLedger ledger) => ledger.Record(ping.Id);
}
