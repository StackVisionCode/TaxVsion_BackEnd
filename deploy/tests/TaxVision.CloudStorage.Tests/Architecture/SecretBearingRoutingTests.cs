using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>
/// Los eventos con el token del enlace compartido en claro tienen un solo consumidor (Notification): van directo a
/// su cola, nunca al fanout que reciben todos los servicios. Se mide sobre el runtime real.
/// </summary>
public sealed class SecretBearingRoutingTests(CloudStorageRoutingHostFactory factory)
    : IClassFixture<CloudStorageRoutingHostFactory>
{
    public static TheoryData<Type> SecretBearingEvents => [typeof(ShareLinkExternalRecipientInvitedIntegrationEvent)];

    [Theory]
    [MemberData(nameof(SecretBearingEvents))]
    public void Messages_that_carry_a_secret_go_only_to_the_notification_queue(Type eventType)
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(eventType);

        var destinations = runtime
            .RoutingFor(eventType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        var routes = string.Join(", ", destinations);
        Assert.True(
            destinations is ["rabbitmq://queue/notification-events"],
            $"{eventType.Name} must go only to the notification-events queue. Routes: {routes}"
        );
        Assert.DoesNotContain(destinations, d => d.Contains("taxvision-events", StringComparison.OrdinalIgnoreCase));
    }
}
