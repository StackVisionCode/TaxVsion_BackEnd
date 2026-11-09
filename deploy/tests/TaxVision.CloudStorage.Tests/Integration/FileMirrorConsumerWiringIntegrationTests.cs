using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Application.Files.Consumers;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>El host real tiene que seguir encontrando el espejo y con el mismo modo de transacción.</summary>
public sealed class FileMirrorConsumerWiringIntegrationTests(CloudStorageApiFactory factory)
    : IClassFixture<CloudStorageApiFactory>
{
    [Fact]
    public void FileAvailable_reaches_the_mirror_consumer()
    {
        var chain = MirrorChain();

        Assert.Contains(chain.Handlers, handler => handler.HandlerType == typeof(CloudStorageFileMirrorConsumer));
    }

    [Fact]
    public void Mirror_consumer_keeps_its_transaction_mode()
    {
        Assert.True(MirrorChain().IsTransactional);
    }

    private HandlerChain MirrorChain()
    {
        return factory.Services.GetRequiredService<HandlerGraph>().ChainFor(typeof(FileAvailableIntegrationEvent))
            ?? throw new InvalidOperationException("FileAvailableIntegrationEvent has no handler chain.");
    }
}
