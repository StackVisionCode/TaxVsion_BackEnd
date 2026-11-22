extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;
using Wolverine;
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

    // N8-03: chat y campañas no ocupan las ranuras de notification-events.
    [Fact]
    public void Bulk_queue_is_durable_and_bounded() => AssertLocalQueue("notification-bulk", 2);

    // N8-04: la llamada a FCM tiene su propia cola y su propio tope.
    [Fact]
    public void Push_queue_is_durable_and_bounded() => AssertLocalQueue("notification-push", 8);

    [Fact]
    public void Lane_commands_are_routed_to_their_queue()
    {
        var tenantId = Guid.NewGuid();
        var push = new PushDispatchRequest(
            tenantId,
            Guid.NewGuid(),
            "t",
            "b",
            NotificationCategory.Collaboration,
            "k",
            "k",
            null,
            null
        );

        Assert.Equal(
            NotificationLanes.Bulk,
            RouteOf(
                new DispatchChatPush(
                    tenantId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    [],
                    null,
                    DateTime.UtcNow,
                    null
                )
            )
        );
        Assert.Equal(
            NotificationLanes.Bulk,
            RouteOf(
                new DispatchCampaignMessage(
                    tenantId,
                    new CampaignDispatchRequestedIntegrationEvent
                    {
                        CampaignId = Guid.NewGuid(),
                        RunId = Guid.NewGuid(),
                        RecipientId = Guid.NewGuid(),
                        AttemptNo = 1,
                        DispatchId = "d-1",
                        Channel = "Email",
                        ContactRef = "c-1",
                    }
                )
            )
        );
        Assert.Equal(NotificationLanes.Push, RouteOf(new DeliverPush(tenantId, push)));
    }

    private string RouteOf(object message)
    {
        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return Assert.Single(bus.PreviewSubscriptions(message)).Destination!.Host;
    }

    private void AssertLocalQueue(string name, int parallelism)
    {
        var runtime = Services.GetRequiredService<IWolverineRuntime>();
        var queue = runtime.Options.Transports.AllEndpoints().SingleOrDefault(e => e.EndpointName == name);

        Assert.NotNull(queue);
        queue.Compile(runtime);
        Assert.Equal(EndpointMode.Durable, queue.Mode);
        Assert.Equal(parallelism, queue.MaxDegreeOfParallelism);
    }
}
