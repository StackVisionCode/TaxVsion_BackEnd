using System.Collections.Concurrent;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.SqlServer;
using Xunit.Abstractions;

namespace TaxVision.Notification.Tests.Integration;

public sealed record LatencyChatInline(int Index);

public sealed record LatencyChatForwarded(int Index);

public sealed record LatencyBulkWork(int Index);

public sealed record LatencyCritical(string Scenario, DateTimeOffset PublishedAt);

public sealed record LatencyBurst(bool Forwarded, int Count);

/// <summary>Lo que mide el banco: cuándo empezó el handler del mensaje crítico de cada escenario.</summary>
public sealed class LatencyProbe
{
    public const int FcmMilliseconds = 40;
    public const int RecipientsPerChat = 2;

    public ConcurrentDictionary<string, TimeSpan> Waits { get; } = new();
}

/// <summary>Antes de N8-03: el push de chat recorre a sus destinatarios dentro de la cola de entrada.</summary>
public static class LatencyChatInlineConsumer
{
    public static async Task Handle(LatencyChatInline message, CancellationToken ct)
    {
        for (var i = 0; i < LatencyProbe.RecipientsPerChat; i++)
            await Task.Delay(LatencyProbe.FcmMilliseconds, ct);
    }
}

/// <summary>Después de N8-03: la cola de entrada solo pasa el mensaje al carril masivo.</summary>
public static class LatencyChatForwardedConsumer
{
    public static LatencyBulkWork Handle(LatencyChatForwarded message) => new(message.Index);

    public static async Task Handle(LatencyBulkWork message, CancellationToken ct)
    {
        for (var i = 0; i < LatencyProbe.RecipientsPerChat; i++)
            await Task.Delay(LatencyProbe.FcmMilliseconds, ct);
    }
}

/// <summary>La ráfaga entra de una vez, como un lote que llega del broker.</summary>
public static class LatencyBurstConsumer
{
    public static IEnumerable<object> Handle(LatencyBurst burst) =>
        Enumerable
            .Range(0, burst.Count)
            .Select(i => burst.Forwarded ? (object)new LatencyChatForwarded(i) : new LatencyChatInline(i));
}

public static class LatencyCriticalConsumer
{
    public static void Handle(LatencyCritical message, LatencyProbe probe) =>
        probe.Waits[message.Scenario] = DateTimeOffset.UtcNow - message.PublishedAt;
}

/// <summary>
/// N8-03 (medición): espera en cola de un mensaje crítico (un código de acceso) con una ráfaga de 500 mensajes de
/// chat en curso. Banco sintético sobre Wolverine y el almacén durable de SQL Server local, con la misma topología
/// de colas que Notification (entrada 5 en paralelo; carril masivo 2) y FCM simulado con una espera fija.
/// </summary>
public sealed class NotificationLaneLatencyIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Burst = 500;
    private const string EventsBefore = "lat-events-before";
    private const string EventsAfter = "lat-events-after";
    private const string Bulk = "lat-bulk";

    private readonly NotificationTempDatabase _database = new("notlat_");
    private readonly LatencyProbe _probe = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(LatencyChatInlineConsumer));
                options.Discovery.IncludeType(typeof(LatencyChatForwardedConsumer));
                options.Discovery.IncludeType(typeof(LatencyCriticalConsumer));
                options.Discovery.IncludeType(typeof(LatencyBurstConsumer));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_database.ConnectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Policies.UseDurableLocalQueues();

                options.PublishMessage<LatencyChatInline>().ToLocalQueue(EventsBefore);
                options.LocalQueue(EventsBefore).MaximumParallelMessages(5).UseDurableInbox();

                options.PublishMessage<LatencyChatForwarded>().ToLocalQueue(EventsAfter);
                options.LocalQueue(EventsAfter).MaximumParallelMessages(5).UseDurableInbox();
                options.PublishMessage<LatencyBulkWork>().ToLocalQueue(Bulk);
                options.LocalQueue(Bulk).MaximumParallelMessages(2).UseDurableInbox();

                options.Services.AddSingleton(_probe);
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Critical_message_does_not_wait_behind_a_chat_burst()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();

        // Calienta los handlers: el primer mensaje de cada tipo compila su código.
        await CriticalWaitAsync(bus, "warmup-before", EventsBefore, forwarded: null);
        await CriticalWaitAsync(bus, "warmup-after", EventsAfter, forwarded: null);
        await bus.InvokeAsync(new LatencyBurst(Forwarded: false, Count: 1));
        await bus.InvokeAsync(new LatencyBurst(Forwarded: true, Count: 1));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var idle = await CriticalWaitAsync(bus, "idle", EventsAfter, forwarded: null);
        var before = await CriticalWaitAsync(bus, "before", EventsBefore, forwarded: false);
        var after = await CriticalWaitAsync(bus, "after", EventsAfter, forwarded: true);

        output.WriteLine($"Sin ráfaga: {idle.TotalMilliseconds:F0} ms");
        output.WriteLine($"Antes (chat en la cola de entrada): {before.TotalMilliseconds:F0} ms");
        output.WriteLine($"Después (chat en el carril masivo): {after.TotalMilliseconds:F0} ms");
        Assert.True(after < before / 3, $"after={after}, before={before}");
    }

    private async Task<TimeSpan> CriticalWaitAsync(IMessageBus bus, string scenario, string queue, bool? forwarded)
    {
        if (forwarded is { } lane)
            await bus.InvokeAsync(new LatencyBurst(lane, Burst));
        await bus.EndpointFor(new Uri($"local://{queue}"))
            .SendAsync(new LatencyCritical(scenario, DateTimeOffset.UtcNow));

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (!_probe.Waits.ContainsKey(scenario) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        // Deja vaciar el carril masivo antes del siguiente escenario.
        await Task.Delay(TimeSpan.FromSeconds(forwarded is null ? 0 : 1));
        return _probe.Waits.TryGetValue(scenario, out var wait)
            ? wait
            : throw new TimeoutException($"Critical message of '{scenario}' was not handled.");
    }
}
