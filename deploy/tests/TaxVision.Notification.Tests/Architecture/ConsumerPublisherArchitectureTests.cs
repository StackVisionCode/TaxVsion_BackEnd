using System.Reflection;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests.Architecture;

public sealed class ConsumerPublisherArchitectureTests
{
    // Eventos sin publicador en todo src desde 2026-11 (D-N47): sus records se quedan, sus consumers no.
    private static readonly Type[] EventsWithoutPublisher =
    [
        typeof(SignerPartialCopyReadyIntegrationEvent),
        typeof(SignatureReadyForDownloadIntegrationEvent),
        typeof(SignatureCertificateReadyForDownloadIntegrationEvent),
    ];

    [Fact]
    public void No_consumer_handles_an_event_without_a_publisher()
    {
        var offenders = typeof(NotificationDispatcher)
            .Assembly.GetTypes()
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(method => method.Name is "Handle" or "HandleAsync" or "Consume" or "ConsumeAsync")
                    .Where(method =>
                        method.GetParameters() is { Length: > 0 } parameters
                        && EventsWithoutPublisher.Contains(parameters[0].ParameterType)
                    )
                    .Select(method => $"{type.Name}.{method.Name}({method.GetParameters()[0].ParameterType.Name})")
            )
            .ToArray();

        Assert.True(offenders.Length == 0, "Consumer de un evento sin publicador:\n" + string.Join("\n", offenders));
    }
}
