using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// BB-2 contra el SQL Server real: con <c>UseTaxVisionDurableLocalQueues()</c> un mensaje local que no se llegó a
/// procesar sigue ahí después de reiniciar el host. En el primer host el handler se queda esperando y el host se
/// apaga sin que termine; en el segundo, sí termina. Sin la pieza, la cola local vive en memoria y el mensaje se pierde al apagar.
/// Esquema propio por prueba, borrado al terminar.
/// </summary>
public sealed class DurableLocalQueueIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = BuildingBlocksTestDatabase.NewSchema("bb2test");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => BuildingBlocksTestDatabase.DropSchemaAsync(_schema);

    [Fact(Timeout = 180_000)]
    public async Task LocalMessage_SurvivesRestart()
    {
        var id = Guid.NewGuid();
        var firstRun = new LocalProbeRecorder();
        using (var host = await StartHostAsync(typeof(PendingLocalProbeHandler), firstRun))
        {
            await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new LocalProbeMessage(id));
            Assert.True(await firstRun.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(30)) == id, "El primer host no recibió el mensaje.");
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stopTimeout.Token);
        }

        var secondRun = new LocalProbeRecorder();
        using (var host = await StartHostAsync(typeof(LocalProbeHandler), secondRun))
        {
            var processed = await Task.WhenAny(secondRun.Processed.Task, Task.Delay(TimeSpan.FromSeconds(45)));
            await host.StopAsync();
            Assert.True(processed == secondRun.Processed.Task, "El mensaje local se perdió con el reinicio.");
            Assert.Equal(id, await secondRun.Processed.Task);
        }
    }

    private async Task<IHost> StartHostAsync(Type handler, LocalProbeRecorder recorder) =>
        await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bb2-durable-local-test";
                options.Discovery.DisableConventionalDiscovery().IncludeType(handler);
                options.Services.AddSingleton(recorder);
                options.PersistMessagesWithSqlServer(BuildingBlocksTestDatabase.ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(100);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                // El primer host no espera al handler retenido para apagarse.
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                options.UseTaxVisionDurableLocalQueues();
            })
            .StartAsync();
}

public sealed record LocalProbeMessage(Guid Id);

public sealed class LocalProbeRecorder
{
    public TaskCompletionSource<Guid> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<Guid> Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Nunca se completa: retiene el handler del primer host.</summary>
    public TaskCompletionSource Hold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public static class PendingLocalProbeHandler
{
    public static async Task Handle(LocalProbeMessage message, LocalProbeRecorder recorder)
    {
        recorder.Attempted.TrySetResult(message.Id);
        await recorder.Hold.Task;
    }
}

public static class LocalProbeHandler
{
    public static void Handle(LocalProbeMessage message, LocalProbeRecorder recorder) => recorder.Processed.TrySetResult(message.Id);
}
