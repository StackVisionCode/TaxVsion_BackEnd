extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.PaymentClient.Tests.Architecture;

// Todo mensaje con handler propio de PaymentClient que va a una cola local (eventos de dominio y el registro del
// webhook) sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(PaymentClientRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<PaymentClientRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
