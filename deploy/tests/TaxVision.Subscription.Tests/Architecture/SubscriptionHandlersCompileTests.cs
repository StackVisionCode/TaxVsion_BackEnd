using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using Wolverine.Runtime;

namespace TaxVision.Subscription.Tests.Architecture;

/// <summary>
/// SUB-11: el host arranca aunque un handler no compile, porque Wolverine genera su código con el primer mensaje.
/// Compila aquí los que cambiaron de dependencias, contra el contenedor real.
/// </summary>
public sealed class SubscriptionHandlersCompileTests(SubscriptionRoutingHostFactory factory)
    : IClassFixture<SubscriptionRoutingHostFactory>
{
    public static TheoryData<Type> Messages =>
        [
            typeof(PurchaseAddOnCommand),
            typeof(ReserveAddOnCheckoutCommand),
            typeof(PurchaseSeatsCommand),
            typeof(SetBillingPreferenceCommand),
        ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void Handler_BuildsInTheRealHost(Type message)
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();

        Assert.NotNull(runtime.Handlers.HandlerFor(message));
    }
}
