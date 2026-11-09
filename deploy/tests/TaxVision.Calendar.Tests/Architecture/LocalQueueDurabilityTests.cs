extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

// Todo mensaje con handler propio de Calendar que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(CalendarRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<CalendarRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
