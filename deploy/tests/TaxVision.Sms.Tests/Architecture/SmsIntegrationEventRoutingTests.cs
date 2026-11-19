using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace TaxVision.Sms.Tests.Architecture;

// Sin PublishMessage<T>() en Program.cs, Wolverine descarta el evento en silencio. Se mide sobre el runtime real.
public sealed class SmsIntegrationEventRoutingTests(SmsRoutingHostFactory factory)
    : IClassFixture<SmsRoutingHostFactory>
{
    public static TheoryData<Type> SmsEvents()
    {
        var data = new TheoryData<Type>();
        foreach (
            var type in typeof(SmsMessageSentIntegrationEvent)
                .Assembly.GetTypes()
                .Where(t =>
                    t.Namespace == typeof(SmsMessageSentIntegrationEvent).Namespace
                    // Los SmsMessage* los publica Sms; SmsSendRequested lo consume.
                    && t.Name.StartsWith("SmsMessage", StringComparison.Ordinal)
                    && !t.IsAbstract
                    && typeof(IntegrationEvent).IsAssignableFrom(t)
                )
                .OrderBy(t => t.Name)
        )
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(SmsEvents))]
    public void Every_sms_integration_event_is_routed_to_the_events_exchange(Type eventType)
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(eventType);

        var destinations = runtime
            .RoutingFor(eventType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        Assert.Contains(
            destinations,
            d => d.StartsWith("rabbitmq://", StringComparison.OrdinalIgnoreCase) && d.Contains("taxvision-events")
        );
    }
}
