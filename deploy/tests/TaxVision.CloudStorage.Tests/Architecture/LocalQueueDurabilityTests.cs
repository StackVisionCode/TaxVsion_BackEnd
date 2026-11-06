extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Architecture;

// CST-17: todo envío local de CloudStorage sobrevive a un reinicio, sin depender de cada cola.
public sealed class LocalQueueDurabilityTests(CloudStorageRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<CloudStorageRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
