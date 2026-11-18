using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace TaxVision.Wallet.Tests.Architecture;

/// <summary>
/// Cada evento que publica Wallet sale hacia RabbitMQ. Sin su <c>PublishMessage&lt;T&gt;()</c>, Wolverine lo descarta
/// en silencio. Se mide sobre el runtime real, no sobre el texto de Program.cs.
/// </summary>
public sealed class IntegrationEventRoutingTests(WalletRoutingHostFactory factory)
    : IClassFixture<WalletRoutingHostFactory>
{
    public static TheoryData<Type> PublishedEvents() =>
        new()
        {
            typeof(WalletTopUpDueIntegrationEvent),
            typeof(WalletBalanceChangedIntegrationEvent),
            typeof(WalletCreditAppliedIntegrationEvent),
            typeof(WalletLowBalanceIntegrationEvent),
            typeof(WalletAutoRechargeSucceededIntegrationEvent),
            typeof(WalletAutoRechargePausedIntegrationEvent),
            typeof(WalletAutoRechargeDailyLimitReachedIntegrationEvent),
        };

    [Theory]
    [MemberData(nameof(PublishedEvents))]
    public void Every_published_wallet_event_is_routed_to_the_broker(Type eventType)
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
            $"{eventType.Name} has no RabbitMQ route (add PublishMessage<{eventType.Name}>() in Program.cs)."
        );
    }
}
