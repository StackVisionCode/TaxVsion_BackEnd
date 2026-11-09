using Microsoft.Extensions.DependencyInjection;
using Wolverine.Configuration;
using Wolverine.Runtime;

namespace TaxVision.Scribe.Tests.Architecture;

// SCR-05: el reintento programado de FileSaveRejected vive en esta cola, así que tiene que ser durable.
public sealed class ConsumerQueueDurabilityTests(ScribeRoutingHostFactory factory)
    : IClassFixture<ScribeRoutingHostFactory>
{
    [Fact]
    public void Scribe_events_queue_is_durable()
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();

        var queue = runtime.Options.Transports.AllEndpoints().Single(e => e.EndpointName == "scribe-events");
        // Con los transportes anulados el host no aplica la configuración de la cola por su cuenta.
        queue.Compile(runtime);

        Assert.Equal(EndpointMode.Durable, queue.Mode);
    }
}
