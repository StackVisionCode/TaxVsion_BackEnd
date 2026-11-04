using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Web.Hosting;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Offboarding;

/// <summary>
/// La política del acuse del retiro contra un host real de Wolverine (codegen incluido, transportes anulados):
/// un acuse por handler de <see cref="UserOffboardedIntegrationEvent"/> en la misma cadena y ninguno si falla.
/// </summary>
public sealed class OffboardingStepAcknowledgementPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Publishes_one_acknowledgement_per_handler_of_the_offboarded_event()
    {
        using var host = await StartAsync(typeof(FirstOffboardedDemoConsumer), typeof(SecondOffboardedDemoConsumer));
        var message = Offboarded();

        var session = await host.InvokeMessageAndWaitAsync(message);

        var acks = session.Sent.MessagesOf<UserOffboardingStepCompletedIntegrationEvent>().ToList();
        Assert.Equal(
            new[] { "FirstOffboardedDemoConsumer", "SecondOffboardedDemoConsumer" },
            acks.Select(a => a.Step).Order().ToArray()
        );
        Assert.All(
            acks,
            ack =>
            {
                Assert.Equal("BuildingBlocks", ack.Service);
                Assert.Equal(message.UserId, ack.UserId);
                Assert.Equal(TenantId, ack.TenantId);
                Assert.Equal(message.CorrelationId, ack.CorrelationId);
            }
        );
    }

    [Fact]
    public async Task Does_not_acknowledge_when_a_handler_of_the_chain_fails()
    {
        using var host = await StartAsync(typeof(FirstOffboardedDemoConsumer), typeof(FailingOffboardedDemoConsumer));

        var session = await host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(c => c.SendAsync(Offboarded()));

        Assert.Empty(session.Sent.MessagesOf<UserOffboardingStepCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Leaves_other_message_types_alone()
    {
        using var host = await StartAsync(typeof(FirstOffboardedDemoConsumer), typeof(DeactivatedDemoConsumer));

        var session = await host.InvokeMessageAndWaitAsync(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = Guid.NewGuid(),
                Email = "a@b.test",
                ActorType = "TenantEmployee",
            }
        );

        Assert.Empty(session.Sent.MessagesOf<UserOffboardingStepCompletedIntegrationEvent>());
    }

    [Theory]
    [InlineData(typeof(OffboardingStepAcknowledgementPolicyTests), "BuildingBlocks")]
    [InlineData(typeof(string), "System.Private.CoreLib")]
    public void Service_comes_from_the_handler_assembly(Type type, string expected) =>
        Assert.Equal(expected, OffboardingStepAcknowledgements.ServiceOf(type));

    private static UserOffboardedIntegrationEvent Offboarded() =>
        new()
        {
            TenantId = TenantId,
            UserId = Guid.NewGuid(),
            Email = "leaver@office.test",
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
            CorrelationId = "corr-ack",
        };

    private static async Task<IHost> StartAsync(params Type[] handlers)
    {
        var host = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                foreach (var handler in handlers)
                    options.Discovery.IncludeType(handler);
                options.AcknowledgeOffboardingSteps();
                options.StubAllExternalTransports();
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}

public static class FirstOffboardedDemoConsumer
{
    public static Task Handle(UserOffboardedIntegrationEvent msg) => Task.CompletedTask;
}

public static class SecondOffboardedDemoConsumer
{
    public static void Handle(UserOffboardedIntegrationEvent msg) { }
}

public static class FailingOffboardedDemoConsumer
{
    public static void Handle(UserOffboardedIntegrationEvent msg) =>
        throw new InvalidOperationException("offboarding step failed");
}

public static class DeactivatedDemoConsumer
{
    public static void Handle(UserDeactivatedIntegrationEvent msg) { }
}
