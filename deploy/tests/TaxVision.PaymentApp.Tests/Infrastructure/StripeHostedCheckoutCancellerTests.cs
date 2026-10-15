using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using CheckoutSession = Stripe.Checkout.Session;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

/// <summary>Mapeo de la respuesta del SDK a <c>Result</c>: una StripeException con código/mensaje de
/// "ya expiró/completó" se trata como éxito idempotente; el resto como fallo.</summary>
public sealed class StripeHostedCheckoutCancellerTests
{
    [Fact]
    public async Task Returns_success_with_alreadyExpired_false_when_expire_succeeds()
    {
        var canceller = new StripeHostedCheckoutCanceller(
            (id, ct) => Task.FromResult(new CheckoutSession { Id = id, Status = "expired" }),
            NullLogger<StripeHostedCheckoutCanceller>.Instance
        );

        var result = await canceller.CancelAsync("cs_live_123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AlreadyExpired);
    }

    [Theory]
    [InlineData("session_already_expired", "whatever")]
    [InlineData("session_completed", "whatever")]
    [InlineData(null, "The checkout session is already expired.")]
    [InlineData(null, "This session is already completed and cannot be expired.")]
    public async Task Treats_already_terminal_errors_as_idempotent_success(string? code, string message)
    {
        var ex = new StripeException(
            System.Net.HttpStatusCode.BadRequest,
            new StripeError { Code = code, Message = message },
            message
        );
        var canceller = new StripeHostedCheckoutCanceller(
            (_, _) => Task.FromException<CheckoutSession>(ex),
            NullLogger<StripeHostedCheckoutCanceller>.Instance
        );

        var result = await canceller.CancelAsync("cs_live_123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyExpired);
    }

    [Fact]
    public async Task Maps_unexpected_stripe_errors_to_a_failure_result()
    {
        var ex = new StripeException(
            System.Net.HttpStatusCode.InternalServerError,
            new StripeError { Code = "api_connection_error", Message = "boom" },
            "boom"
        );
        var canceller = new StripeHostedCheckoutCanceller(
            (_, _) => Task.FromException<CheckoutSession>(ex),
            NullLogger<StripeHostedCheckoutCanceller>.Instance
        );

        var result = await canceller.CancelAsync("cs_live_123", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Stripe.CancelFailed", result.Error.Code);
    }
}
