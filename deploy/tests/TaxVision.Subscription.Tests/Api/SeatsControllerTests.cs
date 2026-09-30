using BuildingBlocks.Web.RateLimiting;
using TaxVision.Subscription.Api.Controllers;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// Iniciar un cobro de asientos va en la categoría L (financiera), no en la G de escribir cualquier cosa:
/// asignar o liberar un asiento no mueve dinero y no debe compartir cupo con comprar.
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

    [Theory]
    [InlineData(nameof(SeatsController.Assign))]
    [InlineData(nameof(SeatsController.Release))]
    [InlineData(nameof(SeatsController.Reassign))]
    public void Moving_a_seat_between_users_stays_a_plain_write(string action)
    {
        Assert.Equal("subscription.g.seat_manage", Policy(action));
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
