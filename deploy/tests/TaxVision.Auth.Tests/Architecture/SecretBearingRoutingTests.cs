using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// Los mensajes de Auth que llevan un código o un token son órdenes a Notification: van solo a su cola y
/// nunca al fanout <c>taxvision-events</c>, que copia todo a las colas de los demás servicios.
/// Se mide sobre el runtime real, no sobre el texto de Program.cs.
/// </summary>
public sealed class SecretBearingRoutingTests(AuthRoutingHostFactory factory) : IClassFixture<AuthRoutingHostFactory>
{
    public static TheoryData<Type> CodeBearingMessages() =>
        new()
        {
            typeof(MfaChallengeRequestedIntegrationEvent),
            typeof(OnboardingOtpRequestedIntegrationEvent),
            typeof(OnboardingPaymentFailedNotificationRequestedIntegrationEvent),
            typeof(PasswordResetRequestedIntegrationEvent),
            typeof(InvitationCreatedIntegrationEvent),
            typeof(EmailChangeRequestedIntegrationEvent),
        };

    [Theory]
    [MemberData(nameof(CodeBearingMessages))]
    public void Messages_that_carry_a_code_go_only_to_the_notification_queue(Type messageType)
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(messageType);

        var destinations = runtime
            .RoutingFor(messageType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        var routes = string.Join(", ", destinations);
        Assert.True(
            destinations is ["rabbitmq://queue/notification-events"],
            $"{messageType.Name} must go only to the notification-events queue. Routes: {routes}"
        );
        Assert.DoesNotContain(destinations, d => d.Contains("taxvision-events", StringComparison.OrdinalIgnoreCase));
    }
}
