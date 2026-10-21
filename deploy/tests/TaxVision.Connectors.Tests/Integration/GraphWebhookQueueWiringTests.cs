using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// El webhook de Graph responde 200 apenas encola. Los tests unitarios llaman a los handlers directo y no
/// ven dos cosas que solo existen en el host real: que Wolverine pueda armar el handler con el
/// contenedor (la codegen es dinámica: un DI roto revienta recién con el primer mensaje) y que el
/// mensaje vaya a la cola local DURABLE. Si la cola no fuera durable, un reinicio entre el 200 y el
/// proceso perdería el aviso, y Graph no lo vuelve a mandar.
/// </summary>
public sealed class GraphWebhookQueueWiringTests(ConnectorsApiFactory factory) : IClassFixture<ConnectorsApiFactory>
{
    public static TheoryData<object> QueuedMessages =>
        [
            new GraphNotificationReceived("sub-1", "correlation"),
            new GraphLifecycleNotificationReceived("sub-1", GraphLifecycleEvents.SubscriptionRemoved, "correlation"),
        ];

    [Theory]
    [MemberData(nameof(QueuedMessages))]
    public void Handler_CompilesAgainstTheRealContainer(object message)
    {
        var graph = factory.Services.GetServices<ICodeFileCollection>().OfType<HandlerGraph>().Single();

        Assert.NotNull(graph.HandlerFor(message.GetType()));
    }

    [Theory]
    [MemberData(nameof(QueuedMessages))]
    public void Message_IsRoutedOnlyToTheDurableLocalQueue(object message)
    {
        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();

        var envelope = Assert.Single(bus.PreviewSubscriptions(message));

        Assert.Equal(new Uri("local://graph-webhook"), envelope.Destination);
        Assert.Equal(EndpointMode.Durable, runtime.Endpoints.EndpointFor(envelope.Destination!)!.Mode);
    }
}
