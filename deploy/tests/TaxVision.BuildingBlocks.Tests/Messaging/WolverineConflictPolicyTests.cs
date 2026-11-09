using System.Diagnostics;
using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// BB-11. Un conflicto de concurrencia por <c>InvokeAsync</c> (HTTP) sale de inmediato; por mensaje se
/// reprograma y después va a la cola de errores. Host real de Wolverine con transportes anulados.
/// </summary>
public sealed class WolverineConflictPolicyTests
{
    private static readonly TimeSpan[] ShortDelays =
    [
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(450),
    ];

    [Fact]
    public async Task Invoke_ConcurrencyConflict_ThrowsAtOnceWithoutRetry()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);
        // Calienta el handler: su compilación no cuenta en el reloj (en el CI cargado tardó más de 20 s).
        await host.MessageBus().InvokeAsync(new ConcurrencyConflictProbe(FailuresBeforeSuccess: 0));

        var clock = Stopwatch.StartNew();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            host.MessageBus().InvokeAsync(new ConcurrencyConflictProbe(FailuresBeforeSuccess: 2))
        );

        Assert.Equal(WolverineConflictPolicy.ConcurrencyConflictCode, conflict.Code);
        // El calentamiento y un solo intento: sin reintento en línea.
        Assert.Equal(2, probe.AttemptsOf<ConcurrencyConflictProbe>().Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Tardó {clock.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task Message_ConcurrencyConflict_IsRescheduledUntilItPasses()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);

        await host.MessageBus().SendAsync(new ConcurrencyConflictProbe(FailuresBeforeSuccess: 2));
        await probe.WaitForAttemptsAsync<ConcurrencyConflictProbe>(3, TimeSpan.FromSeconds(20));

        var attempts = probe.AttemptsOf<ConcurrencyConflictProbe>();
        for (var i = 1; i < attempts.Count; i++)
            Assert.True(
                attempts[i] - attempts[i - 1] >= ShortDelays[i - 1] * 0.8,
                $"El reintento {i} no esperó su escalón."
            );
    }

    [Fact]
    public async Task Message_ConflictOnEveryAttempt_EndsInTheErrorQueue()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);

        var session = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(c => c.SendAsync(new ConcurrencyConflictProbe(int.MaxValue)));

        // Un intento inicial más uno por cada espera.
        Assert.Equal(ShortDelays.Length + 1, probe.AttemptsOf<ConcurrencyConflictProbe>().Count);
        Assert.Single(session.MovedToErrorQueue.MessagesOf<ConcurrencyConflictProbe>());
    }

    [Fact]
    public async Task Message_SingleDelay_RetriesOnceThenErrorQueue()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe, [TimeSpan.FromMilliseconds(150)]);

        var session = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(c => c.SendAsync(new ConcurrencyConflictProbe(int.MaxValue)));

        Assert.Equal(2, probe.AttemptsOf<ConcurrencyConflictProbe>().Count);
        Assert.Single(session.MovedToErrorQueue.MessagesOf<ConcurrencyConflictProbe>());
    }

    // Lo que se publica antes del choque no sale por el camino del mensaje: el intento se descarta entero.
    [Fact]
    public async Task Message_PublishedBeforeTheConflict_LeavesOnlyFromTheAttemptThatPasses()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);

        await host.MessageBus().SendAsync(new PublishThenConflictProbe(FailuresBeforeSuccess: 2));
        await probe.WaitForAttemptsAsync<PublishedBeforeConflict>(1, TimeSpan.FromSeconds(20));
        await Task.Delay(500);

        Assert.Equal(3, probe.AttemptsOf<PublishThenConflictProbe>().Count);
        Assert.Single(probe.AttemptsOf<PublishedBeforeConflict>());
    }

    [Fact]
    public async Task Invoke_PublishedBeforeTheConflict_DoesNotLeave()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);

        await Assert.ThrowsAsync<ConflictException>(() =>
            host.MessageBus().InvokeAsync(new PublishThenConflictProbe(FailuresBeforeSuccess: 1))
        );
        await Task.Delay(500);

        Assert.Empty(probe.AttemptsOf<PublishedBeforeConflict>());
    }

    // El código manda: otro ConflictException sigue con la regla general.
    [Fact]
    public async Task Invoke_OtherConflictCode_KeepsTheStandardRetry()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe);

        await host.MessageBus().InvokeAsync(new OtherConflictProbe(FailuresBeforeSuccess: 1));

        Assert.Equal(2, probe.AttemptsOf<OtherConflictProbe>().Count);
    }

    // Aditiva: sin llamarla, InvokeAsync conserva el reintento en línea.
    [Fact]
    public async Task WithoutThePolicy_InvokeKeepsTheStandardRetry()
    {
        var probe = new AttemptProbe();
        using var host = await StartAsync(probe, usePolicy: false);

        await host.MessageBus().InvokeAsync(new ConcurrencyConflictProbe(FailuresBeforeSuccess: 1));

        Assert.Equal(2, probe.AttemptsOf<ConcurrencyConflictProbe>().Count);
    }

    [Fact]
    public void Options_DefaultDelays_AreTheDocumentedSteps()
    {
        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10),
        ];
        Assert.Equal(expected, ConflictRetryOptions.DefaultDelays);
        Assert.Null(new ConflictRetryOptions().Delays);
    }

    [Fact]
    public void Options_RejectEmptyOrNonPositiveDelays()
    {
        var options = new WolverineOptions();

        Assert.Throws<ArgumentException>(() =>
            options.UseTaxVisionConflictPolicy(new ConflictRetryOptions { Delays = [] })
        );
        Assert.Throws<ArgumentException>(() =>
            options.UseTaxVisionConflictPolicy(new ConflictRetryOptions { Delays = [TimeSpan.Zero] })
        );
    }

    private static async Task<IHost> StartAsync(AttemptProbe probe, TimeSpan[]? delays = null, bool usePolicy = true)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(probe))
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(ConflictPolicyProbeHandler));
                options.StubAllExternalTransports();
                if (usePolicy)
                    options.UseTaxVisionConflictPolicy(new ConflictRetryOptions { Delays = delays ?? ShortDelays });
                options.ApplyStandardFailurePolicies();
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}

public sealed record ConcurrencyConflictProbe(int FailuresBeforeSuccess);

public sealed record OtherConflictProbe(int FailuresBeforeSuccess);

public sealed record PublishThenConflictProbe(int FailuresBeforeSuccess);

public sealed record PublishedBeforeConflict;

public static class ConflictPolicyProbeHandler
{
    public static void Handle(ConcurrencyConflictProbe message, AttemptProbe probe)
    {
        probe.Record(typeof(ConcurrencyConflictProbe));
        if (probe.AttemptsOf<ConcurrencyConflictProbe>().Count <= message.FailuresBeforeSuccess)
            throw new ConflictException(WolverineConflictPolicy.ConcurrencyConflictCode, "Conflict.");
    }

    public static void Handle(OtherConflictProbe message, AttemptProbe probe)
    {
        probe.Record(typeof(OtherConflictProbe));
        if (probe.AttemptsOf<OtherConflictProbe>().Count <= message.FailuresBeforeSuccess)
            throw new ConflictException("Persistence.UniqueConstraint", "Duplicate.");
    }

    public static async Task Handle(PublishThenConflictProbe message, AttemptProbe probe, IMessageBus bus)
    {
        probe.Record(typeof(PublishThenConflictProbe));
        await bus.PublishAsync(new PublishedBeforeConflict());
        if (probe.AttemptsOf<PublishThenConflictProbe>().Count <= message.FailuresBeforeSuccess)
            throw new ConflictException(WolverineConflictPolicy.ConcurrencyConflictCode, "Conflict.");
    }

    public static void Handle(PublishedBeforeConflict message, AttemptProbe probe) =>
        probe.Record(typeof(PublishedBeforeConflict));
}
