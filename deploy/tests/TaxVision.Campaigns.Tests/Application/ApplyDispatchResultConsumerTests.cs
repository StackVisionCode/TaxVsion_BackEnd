using System.Reflection;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Runs.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>
/// CMP-14: un resultado sin su run no se acusa como procesado: se reintenta y termina visible en la DLQ. Un dato
/// inválido del ejecutor va directo a la DLQ; una unidad desconocida dentro de un run existente se registra y sigue.
/// </summary>
public sealed class ApplyDispatchResultConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Handle_RunNotFound_ThrowsDependencyNotReady()
    {
        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            RunAsync(typeof(ApplyDispatchResultConsumer), Result(Guid.NewGuid(), "dispatch-1", "Delivered"))
        );
    }

    [Fact]
    public async Task Handle_UnknownOutcome_ThrowsArgumentException()
    {
        var run = Dispatched();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            RunAsync(
                typeof(ApplyDispatchResultConsumer),
                Result(run.Id, run.Recipients.Single().DispatchId, "Teleported"),
                run
            )
        );
    }

    [Fact]
    public async Task Handle_UnknownUnit_IsLoggedAndAcknowledged()
    {
        var run = Dispatched();

        await RunAsync(typeof(ApplyDispatchResultConsumer), Result(run.Id, "no-such-unit", "Delivered"), run);

        Assert.Equal(CampaignRunStatus.Dispatching, run.Status);
    }

    [Fact]
    public async Task EmailReport_RunNotFound_ThrowsDependencyNotReady()
    {
        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            RunAsync(typeof(CampaignEmailDeliveredConsumer), EmailDelivered(Guid.NewGuid()))
        );
    }

    /// <summary>Un correo que no es de campaña no tiene run que esperar.</summary>
    [Fact]
    public async Task EmailReport_NotACampaignMail_IsIgnored()
    {
        await RunAsync(typeof(CampaignEmailDeliveredConsumer), EmailDelivered(campaignSeam: null));
    }

    [Fact]
    public async Task SmsReport_RunNotFound_ThrowsDependencyNotReady()
    {
        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            RunAsync(typeof(CampaignSmsDeliveredConsumer), Delivered("campaign:dispatch-1"))
        );
    }

    /// <summary>Un SMS que no es de campaña no tiene run que esperar.</summary>
    [Fact]
    public async Task SmsReport_NotACampaignMessage_IsIgnored()
    {
        await RunAsync(typeof(CampaignSmsDeliveredConsumer), Delivered("appointment:reminder"));
    }

    private static PostmasterEmailDeliverySucceededIntegrationEvent EmailDelivered(Guid? campaignSeam) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "test",
            NotificationLogId = Guid.NewGuid(),
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            EventAtUtc = DateTime.UtcNow,
            CampaignId = campaignSeam,
            ProviderMessageId = "prov-1",
        };

    private static SmsMessageDeliveredIntegrationEvent Delivered(string sourceContext) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "test",
            MessageId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            SourceContext = sourceContext,
            ProviderMessageId = "prov-1",
        };

    private static CampaignDispatchResultIntegrationEvent Result(Guid runId, string dispatchId, string outcome) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "test",
            CampaignId = Guid.NewGuid(),
            RunId = runId,
            DispatchId = dispatchId,
            Outcome = outcome,
        };

    private static CampaignRun Dispatched()
    {
        var run = CampaignRun
            .Start(
                Tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [new RunRecipientDraft("c1", CampaignChannel.Email, "a@x.com", null)]
            )
            .Value;
        run.MarkFundsReserved();
        run.MarkDispatched(run.Recipients.Single().Id);
        return run;
    }

    // Resuelve los parámetros por tipo: la firma de los consumidores no es parte de esta prueba.
    private static async Task RunAsync(Type consumer, object message, params CampaignRun[] seed)
    {
        var runs = new FakeCampaignRunRepository();
        runs.Runs.AddRange(seed);
        var services = new List<object>
        {
            runs,
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
        };
        var method = consumer
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == message.GetType());
        var args = method
            .GetParameters()
            .Select(p =>
                p.ParameterType == message.GetType() ? message
                : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                : p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)
                    ? Activator.CreateInstance(
                        typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments())
                    )
                : services.First(s => p.ParameterType.IsInstanceOfType(s))
            )
            .ToArray();

        await (Task)method.Invoke(null, args)!;
    }
}
