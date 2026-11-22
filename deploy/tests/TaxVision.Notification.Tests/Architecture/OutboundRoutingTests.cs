using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.Notification.Tests.Architecture;

/// <summary>
/// El correo renderizado y el SMS llevan el texto final (códigos, enlaces): van solo a la cola de su único
/// consumidor y nunca al fanout <c>taxvision-events</c>. Se mide sobre el runtime real, no sobre el texto.
/// </summary>
public sealed class OutboundRoutingTests(NotificationRoutingHostFactory factory, ITestOutputHelper output)
    : IClassFixture<NotificationRoutingHostFactory>
{
    [Fact]
    public void Rendered_emails_go_only_to_the_postmaster_queue() =>
        AssertOnlyQueue(typeof(NotificationsEmailSendRequestedIntegrationEvent), "postmaster-events");

    [Fact]
    public void Sms_requests_go_only_to_the_sms_queue() =>
        AssertOnlyQueue(typeof(SmsSendRequestedIntegrationEvent), "sms-events");

    // Notification declara la cola destino igual que declara la suya propia, que es como la declaran sus dueños.
    [Theory]
    [InlineData("postmaster-events")]
    [InlineData("sms-events")]
    public void Target_queues_are_declared_like_a_listener_queue(string queueName)
    {
        var transport = factory
            .Services.GetRequiredService<IWolverineRuntime>()
            .Options.Transports.GetOrCreate<RabbitMqTransport>();

        var target = Describe(transport.Queues[queueName]);
        var listener = Describe(transport.Queues["notification-events"]);
        output.WriteLine($"{queueName}: {target}");
        output.WriteLine($"notification-events: {listener}");

        Assert.Equal(listener, target);
    }

    private void AssertOnlyQueue(Type messageType, string queueName)
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(messageType);

        var destinations = runtime
            .RoutingFor(messageType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        var routes = string.Join(", ", destinations);
        output.WriteLine($"{messageType.Name}: {routes}");
        Assert.True(
            destinations.Count == 1 && destinations[0] == $"rabbitmq://queue/{queueName}",
            $"{messageType.Name} must go only to the {queueName} queue. Routes: {routes}"
        );
        Assert.DoesNotContain(destinations, d => d.Contains("taxvision-events", StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(RabbitMqQueue queue)
    {
        var arguments = string.Join(
            ";",
            queue.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")
        );
        return $"durable={queue.IsDurable} exclusive={queue.IsExclusive} autoDelete={queue.AutoDelete} "
            + $"type={queue.QueueType} dlq={queue.DeadLetterQueue?.QueueName}/{queue.DeadLetterQueue?.Mode} "
            + $"args=[{arguments}]";
    }
}
