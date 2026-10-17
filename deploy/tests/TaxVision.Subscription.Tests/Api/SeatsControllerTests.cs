using BuildingBlocks.Web.RateLimiting;
using TaxVision.Subscription.Api.Controllers;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// Iniciar un cobro de asientos va en la categoría L (financiera), no en la G de escribir cualquier cosa:
/// comprar seats mueve dinero y no debe compartir cupo con operaciones no financieras.
/// </summary>
public sealed class SeatsControllerTests
{
    [Theory]
    [InlineData(nameof(SeatsController.Purchase))]
    [InlineData(nameof(SeatsController.StartCheckout))]
    public void Starting_a_charge_uses_the_financial_policy(string action)
    {
        Assert.Equal("subscription.l.seat_purchase", Policy(action));
    }

    // El nombre de la política viaja en el constructor del atributo, no en una propiedad.
    private static string Policy(string action) =>
        (string)
            typeof(SeatsController)
                .GetMethod(action)!
                .GetCustomAttributesData()
                .Single(data => data.AttributeType == typeof(RateLimitAttribute))
                .ConstructorArguments[0]
                .Value!;
}
