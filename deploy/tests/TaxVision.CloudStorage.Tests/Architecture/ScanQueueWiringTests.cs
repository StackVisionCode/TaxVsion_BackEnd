using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Application.Files.Commands;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports.Local;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>La cola de escaneo del host real: durable y con el paralelismo que dice la configuración (CST-16).</summary>
public sealed class ScanQueueWiringTests(CloudStorageRoutingHostFactory factory)
    : IClassFixture<CloudStorageRoutingHostFactory>
{
    private static readonly ScanFileCommand Command = new(Guid.NewGuid(), Guid.NewGuid(), "wiring");

    [Fact]
    public void ScanQueue_IsDurableAndBoundedParallel()
    {
        var queue = ScanQueue(factory.Services);

        Assert.Equal(EndpointMode.Durable, queue.Mode);
        Assert.Equal(2, queue.MaxDegreeOfParallelism);
    }

    [Fact]
    public void ScanQueue_ReadsItsParallelismFromConfiguration()
    {
        using var configured = factory.WithWebHostBuilder(host => host.UseSetting("CloudStorage:ScanParallelism", "3"));

        Assert.Equal(3, ScanQueue(configured.Services).MaxDegreeOfParallelism);
    }

    private static LocalQueue ScanQueue(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var runtime = services.GetRequiredService<IWolverineRuntime>();
        var envelope = Assert.Single(bus.PreviewSubscriptions(Command));
        return Assert.IsType<LocalQueue>(runtime.Endpoints.EndpointFor(envelope.Destination!));
    }
}
