using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Api.Controllers;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.Billing.Commands.StartPaymentMethodSetup;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// El email del pagador de un checkout sale del token del usuario, no del cuerpo: el recibo y la
/// pasarela no pueden ir a un email que no es el de quien compra.
/// </summary>
public sealed class CheckoutPayerEmailTests
{
    private const string TokenEmail = "owner@office.test";
    private const string BodyEmail = "attacker@evil.test";
    private const string Back = "https://app.taxoffix.io/storage";

    [Fact]
    public async Task Add_on_checkout_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new AddOnsController(bus, NullLogger<AddOnsController>.Instance));

        await controller.StartCheckout(
            new AddOnsController.StartAddOnCheckoutRequest("STORAGE_2GB", 1, true, BodyEmail, Back, Back, null, null),
            CancellationToken.None
        );

        Assert.Equal(TokenEmail, Assert.IsType<StartAddOnCheckoutCommand>(captured()).PayerEmail);
    }

    [Fact]
    public async Task Seat_checkout_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SeatsController(bus));

        await controller.StartCheckout(
            new SeatsController.StartSeatCheckoutRequest("Standard", 1, true, BodyEmail, Back, Back, null, null),
            CancellationToken.None
        );

        Assert.Equal(TokenEmail, Assert.IsType<StartSeatCheckoutCommand>(captured()).PayerEmail);
    }

    [Fact]
    public async Task Seat_reactivation_checkout_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SeatsController(bus));

        await controller.StartReactivationCheckout(
            Guid.NewGuid(),
            new SeatsController.StartSeatReactivationCheckoutRequest(BodyEmail, Back, Back, null, null),
            CancellationToken.None
        );

        Assert.Equal(TokenEmail, Assert.IsType<StartSeatReactivationCheckoutCommand>(captured()).PayerEmail);
    }

    [Fact]
    public async Task Renewal_checkout_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SubscriptionsController(bus));

        await controller.StartRenewCheckout(
            new SubscriptionsController.RenewCheckoutRequest(BodyEmail, Back, Back),
            CancellationToken.None
        );

        Assert.Equal(TokenEmail, Assert.IsType<StartRenewalCheckoutCommand>(captured()).PayerEmail);
    }

    [Fact]
    public async Task Plan_change_checkout_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SubscriptionsController(bus));

        await controller.ChangePlan(
            new SubscriptionsController.ChangePlanRequest("ENTERPRISE", null, BodyEmail, Back, Back),
            CancellationToken.None
        );

        var command = Assert.IsType<ChangePlanCommand>(captured());
        Assert.Equal(TokenEmail, command.PayerEmail);
        Assert.True(command.WantsHostedCheckout);
    }

    [Fact]
    public async Task Payment_method_setup_uses_the_token_email()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SubscriptionsController(bus));

        await controller.StartPaymentMethodSetup(
            new SubscriptionsController.StartPaymentMethodSetupRequest(BodyEmail, Back, Back),
            CancellationToken.None
        );

        Assert.Equal(TokenEmail, Assert.IsType<StartPaymentMethodSetupCommand>(captured()).PayerEmail);
    }

    [Fact]
    public async Task Checkout_without_an_email_in_the_token_is_rejected_without_starting()
    {
        var (bus, captured) = Bus();
        var controller = WithUser(new SeatsController(bus), email: null);

        var response = await controller.StartCheckout(
            new SeatsController.StartSeatCheckoutRequest("Standard", 1, true, BodyEmail, Back, Back, null, null),
            CancellationToken.None
        );

        Assert.Null(captured());
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(response).StatusCode);
    }

    private static (CapturingMessageBus Bus, Func<object?> Captured) Bus()
    {
        object? last = null;
        var bus = new CapturingMessageBus
        {
            // Corta el flujo: solo importa qué comando armó el controller.
            OnInvoke = (message, _) =>
            {
                last = message;
                return Task.FromResult<object?>(StopHere(message));
            },
        };
        return (bus, () => last);
    }

    private static object StopHere(object message)
    {
        var error = new Error("Test.Stop", "stop");
        var resultType = message.GetType().Name switch
        {
            nameof(ChangePlanCommand) => typeof(Result<ChangePlanResult>),
            nameof(StartAddOnCheckoutCommand) => typeof(Result<StartAddOnCheckoutResponse>),
            nameof(StartSeatCheckoutCommand) => typeof(Result<StartSeatCheckoutResponse>),
            nameof(StartSeatReactivationCheckoutCommand) => typeof(Result<StartSeatReactivationCheckoutResponse>),
            nameof(StartRenewalCheckoutCommand) => typeof(Result<StartRenewalCheckoutResponse>),
            nameof(StartPaymentMethodSetupCommand) => typeof(Result<StartPaymentMethodSetupResponse>),
            _ => throw new InvalidOperationException(message.GetType().Name),
        };
        var failure = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(resultType.GetGenericArguments()[0]);
        return failure.Invoke(null, [error])!;
    }

    private static T WithUser<T>(T controller, string? email = TokenEmail)
        where T : ControllerBase
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new(ClaimNames.TenantId, Guid.NewGuid().ToString()),
            new(ClaimNames.ActorType, nameof(ActorType.TenantAdmin)),
        };
        if (email is not null)
            claims.Add(new Claim(ClaimTypes.Email, email));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
        return controller;
    }
}
