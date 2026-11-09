extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine.Runtime;

namespace TaxVision.Sms.Tests.Architecture;

// La liquidación pendiente de un SMS que ya salió es dinero: sobrevive a un reinicio y espera al Wallet.
public sealed class LocalQueueDurabilityTests(SmsRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<SmsRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;

    [Fact]
    public void SettleSmsBatchCommand_HasItsOwnRetryRules()
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();

        Assert.NotEmpty(runtime.Handlers.ChainFor(typeof(SettleSmsBatchCommand))!.Failures);
    }
}
