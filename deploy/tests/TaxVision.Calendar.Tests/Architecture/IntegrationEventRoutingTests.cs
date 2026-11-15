using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

/// <summary>
/// Todo evento de integración de Calendar sale hacia RabbitMQ. Sin su <c>PublishMessage&lt;T&gt;()</c>, Wolverine
/// lo descarta en silencio ("No routes can be determined"): así se perdió el aviso "ya tienes una demo" (E-16).
/// Se mide sobre el runtime real, no sobre el texto de Program.cs.
/// </summary>
public sealed class IntegrationEventRoutingTests(CalendarRoutingHostFactory factory)
    : IClassFixture<CalendarRoutingHostFactory>
{
    public static TheoryData<Type> CalendarEvents()
    {
        var data = new TheoryData<Type>();
        foreach (
            var type in typeof(PublicBookingConfirmedIntegrationEvent)
                .Assembly.GetTypes()
                .Where(t =>
                    t.Namespace == typeof(PublicBookingConfirmedIntegrationEvent).Namespace
                    && !t.IsAbstract
                    && typeof(IntegrationEvent).IsAssignableFrom(t)
                )
                .OrderBy(t => t.Name)
        )
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(CalendarEvents))]
    public void Every_calendar_integration_event_is_routed_to_the_broker(Type eventType)
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(eventType);

        var destinations = runtime
            .RoutingFor(eventType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        Assert.True(
            destinations.Any(d => d.StartsWith("rabbitmq://", StringComparison.OrdinalIgnoreCase)),
            $"{eventType.Name} has no RabbitMQ route (add PublishMessage<{eventType.Name}>() in Program.cs). Routes: "
                + string.Join(", ", destinations)
        );
    }
}
