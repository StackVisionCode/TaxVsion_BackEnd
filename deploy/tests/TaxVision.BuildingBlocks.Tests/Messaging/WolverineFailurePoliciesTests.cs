using System.Collections.Concurrent;
using System.Diagnostics;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// BB-7. <see cref="DependencyNotReadyException"/> se reintenta con esperas largas y después va a la
/// cola de errores; el resto de la política estándar no cambia. Host real de Wolverine con transportes anulados.
/// </summary>
[Collection(nameof(WolverineFailurePoliciesTests))]
public sealed class WolverineFailurePoliciesTests
{
    private static readonly TimeSpan[] ShortDelays =
    [
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(450),
        TimeSpan.FromMilliseconds(600),
    ];

    [Fact]
    public async Task DependencyNotReady_IsRetriedWithBackoff()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe, new DependencyNotReadyRetryOptions { Delays = ShortDelays });

        var session = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(c => c.SendAsync(new NotReadyProbe(Guid.NewGuid())));

        // Un intento inicial más uno por cada espera, y después a la cola de errores.
        var attempts = probe.AttemptsOf<NotReadyProbe>();
        Assert.Equal(ShortDelays.Length + 1, attempts.Count);
        Assert.Single(session.MovedToErrorQueue.MessagesOf<NotReadyProbe>());

        // Cada reintento espera al menos su escalón (margen por la resolución del temporizador).
        for (var i = 1; i < attempts.Count; i++)
        {
            var gap = attempts[i] - attempts[i - 1];
            Assert.True(
                gap >= ShortDelays[i - 1] * 0.8,
                $"El reintento {i} llegó a los {gap.TotalMilliseconds:F0} ms; esperaba al menos {ShortDelays[i - 1].TotalMilliseconds} ms."
            );
        }
    }

    [Fact]
    public void DependencyNotReady_DefaultDelays_AreTheDocumentedSteps()
    {
        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10),
        ];
        Assert.Equal(expected, DependencyNotReadyRetryOptions.DefaultDelays);
        Assert.Null(new DependencyNotReadyRetryOptions().Delays);
    }

    [Fact]
    public void DependencyNotReady_IsNotTreatedAsPermanent()
    {
        Assert.DoesNotContain(
            WolverineFailurePolicies.PermanentFailureTypes,
            p => p.IsAssignableFrom(typeof(DependencyNotReadyException))
        );
    }

    [Fact]
    public async Task PermanentFailure_StillGoesStraightToTheErrorQueue()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe, new DependencyNotReadyRetryOptions { Delays = ShortDelays });

        var session = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(c => c.SendAsync(new BadPayloadProbe(Guid.NewGuid())));

        Assert.Single(probe.AttemptsOf<BadPayloadProbe>());
        Assert.Single(session.MovedToErrorQueue.MessagesOf<BadPayloadProbe>());
    }

    [Fact]
    public async Task TransientFailure_KeepsTheStandardCooldowns()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe, new DependencyNotReadyRetryOptions { Delays = ShortDelays });

        await host.MessageBus().SendAsync(new TransientProbe(Guid.NewGuid()));
        await probe.WaitForAttemptsAsync<TransientProbe>(2, TimeSpan.FromSeconds(10));

        // La genérica sigue con 1 s antes del segundo intento: la regla nueva no la alcanza.
        var attempts = probe.AttemptsOf<TransientProbe>();
        Assert.True(attempts[1] - attempts[0] >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public void Options_RejectEmptyOrNonPositiveDelays()
    {
        var options = new WolverineOptions();

        Assert.Throws<ArgumentException>(() =>
            options.ApplyStandardFailurePolicies(new DependencyNotReadyRetryOptions { Delays = [] })
        );
        Assert.Throws<ArgumentException>(() =>
            options.ApplyStandardFailurePolicies(new DependencyNotReadyRetryOptions { Delays = [TimeSpan.Zero] })
        );
    }

    private static async Task<IHost> StartAsync(AttemptProbe probe, DependencyNotReadyRetryOptions retry)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(probe))
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(FailurePolicyProbeHandler));
                options.StubAllExternalTransports();
                options.ApplyStandardFailurePolicies(retry);
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}

/// <summary>
/// Aislada: mide esperas de 1 s entre intentos; con decenas de hosts arrancando en paralelo el primer intento no
/// llegaba dentro del plazo según el orden en que xUnit repartía las clases.
/// </summary>
[CollectionDefinition(nameof(WolverineFailurePoliciesTests), DisableParallelization = true)]
public sealed class WolverineFailurePoliciesCollection;

public sealed record NotReadyProbe(Guid Id);

public sealed record BadPayloadProbe(Guid Id);

public sealed record TransientProbe(Guid Id);

public sealed class AttemptProbe
{
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<TimeSpan>> _attempts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public void Record(Type messageType) =>
        _attempts.GetOrAdd(messageType, _ => new ConcurrentQueue<TimeSpan>()).Enqueue(_clock.Elapsed);

    public IReadOnlyList<TimeSpan> AttemptsOf<T>() =>
        _attempts.TryGetValue(typeof(T), out var queue) ? queue.ToArray() : [];

    public async Task WaitForAttemptsAsync<T>(int count, TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
        while (AttemptsOf<T>().Count < count)
        {
            if (_clock.Elapsed > deadline)
                throw new TimeoutException($"{typeof(T).Name}: {AttemptsOf<T>().Count} de {count} intentos.");
            await Task.Delay(50);
        }
    }
}

public static class FailurePolicyProbeHandler
{
    public static void Handle(NotReadyProbe message, AttemptProbe probe)
    {
        probe.Record(typeof(NotReadyProbe));
        throw new DependencyNotReadyException("probe-projection");
    }

    public static void Handle(BadPayloadProbe message, AttemptProbe probe)
    {
        probe.Record(typeof(BadPayloadProbe));
        throw new FormatException("bad payload");
    }

    public static void Handle(TransientProbe message, AttemptProbe probe)
    {
        probe.Record(typeof(TransientProbe));
        throw new InvalidOperationException("transient");
    }
}
