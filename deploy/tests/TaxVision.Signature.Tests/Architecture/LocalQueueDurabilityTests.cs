extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Signature.Tests.Architecture;

// SIG-BB2: todo mensaje con handler propio de Signature que va a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(SignatureRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<SignatureRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
