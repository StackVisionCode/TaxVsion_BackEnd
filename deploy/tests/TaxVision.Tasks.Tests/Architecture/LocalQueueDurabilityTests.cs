extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Tasks.Tests.Architecture;

// Todo mensaje con handler propio de Tasks que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(TasksRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<TasksRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
