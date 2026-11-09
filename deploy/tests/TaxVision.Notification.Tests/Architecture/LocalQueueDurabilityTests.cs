extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Configuration;
using Wolverine.Runtime;

namespace TaxVision.Notification.Tests.Architecture;

// NOT-18: lo que Notification se manda a sí mismo sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(NotificationRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<NotificationRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;

    // NOT-10: el reintento programado de los callbacks vive en esta cola, así que tiene que ser durable.
    [Fact]
    public void Postmaster_callbacks_queue_is_durable()
    {
        var runtime = Services.GetRequiredService<IWolverineRuntime>();

        var queue = runtime.Options.Transports.AllEndpoints().Single(e => e.EndpointName == "notification-events");
        // Con los transportes anulados el host no aplica la configuración de la cola por su cuenta.
        queue.Compile(runtime);

        Assert.Equal(EndpointMode.Durable, queue.Mode);
    }
}
