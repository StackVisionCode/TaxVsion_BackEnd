using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TaxVision.Subscription.Api.Controllers;

/// <summary>El pagador de un checkout es el usuario del token: su email, no uno del cuerpo.</summary>
internal static class CheckoutPayer
{
    public static readonly Error MissingEmailError = new(
        "Checkout.MissingPayerEmail",
        "The signed-in user has no email to bill."
    );

    public static IActionResult MissingEmail() =>
        new ObjectResult(MissingEmailError) { StatusCode = StatusCodes.Status400BadRequest };
}
