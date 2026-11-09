using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.PaymentClient.Tests.Messaging;

/// <summary>
/// PCL-14 depende de que <see cref="Envelope.SentAt"/> (cuándo publicó Billing) llegue a PaymentClient.
/// Se pasa un sobre por el mapeador de RabbitMQ de Wolverine (salida → propiedades AMQP → entrada) sin broker.
/// </summary>
public sealed class WolverineSentAtHeaderTests
{
    [Fact]
    public async Task SentAt_TravelsThroughTheRabbitMqMapper()
    {
        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.UseRabbitMq(new Uri("amqp://guest:guest@localhost:5672"));
                options.PublishAllMessages().ToRabbitExchange("taxvision-events");
                options.StubAllExternalTransports();
            })
            .StartAsync();

        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        var endpoint = runtime.Endpoints.EndpointFor(new Uri("rabbitmq://exchange/taxvision-events"))!;
        var mapper = new RabbitMqEnvelopeMapper(endpoint, runtime);

        var sentAt = new DateTimeOffset(2026, 11, 4, 23, 17, 0, TimeSpan.Zero);
        var outgoing = new Envelope
        {
            Id = Guid.NewGuid(),
            SentAt = sentAt,
            MessageType = "probe",
        };
        var properties = new BasicProperties { Headers = new Dictionary<string, object?>() };
        mapper.MapEnvelopeToOutgoing(outgoing, properties);

        var incoming = new Envelope();
        mapper.MapIncomingToEnvelope(incoming, properties);

        Assert.Equal(sentAt, incoming.SentAt);

        await host.StopAsync();
    }

    [Fact]
    public void NewEnvelope_IsStampedWithThePublishTime()
    {
        var before = DateTimeOffset.UtcNow;

        var envelope = new Envelope(new object());

        Assert.InRange(envelope.SentAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }
}
