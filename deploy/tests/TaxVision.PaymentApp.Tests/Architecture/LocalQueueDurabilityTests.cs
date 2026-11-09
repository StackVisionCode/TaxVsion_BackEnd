// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.PaymentApp.Tests.Architecture;

// PAY-BB2: todo mensaje con handler propio de PaymentApp que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(PaymentAppRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<PaymentAppRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
