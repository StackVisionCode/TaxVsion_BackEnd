using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

/// <summary>El correo de cobro fallido explica el motivo en palabras de cliente, nunca con el código del proveedor.</summary>
public sealed class ChargeFailureReasonsTests
{
    [Theory]
    [InlineData("card_declined", "Your bank declined the charge.")]
    [InlineData("generic_decline", "Your bank declined the charge.")]
    [InlineData("insufficient_funds", "The card doesn't have enough available funds.")]
    [InlineData("expired_card", "The card on file has expired.")]
    [InlineData("incorrect_cvc", "The card's security code was not accepted.")]
    [InlineData("requires_payment_method", "There is no payment method on file for automatic payments.")]
    [InlineData("PayPal.OffSession.MissingToken", "There is no payment method on file for automatic payments.")]
    [InlineData(
        "authentication_required",
        "Your bank asked for an extra confirmation that can't be completed automatically."
    )]
    [InlineData("processing_error", "The payment processor had a temporary problem.")]
    [InlineData("Provider.Cancelled", ChargeFailureReasons.Generic)]
    [InlineData("", ChargeFailureReasons.Generic)]
    [InlineData(null, ChargeFailureReasons.Generic)]
    public void Explains_the_failure_without_provider_jargon(string? code, string expected)
    {
        Assert.Equal(expected, ChargeFailureReasons.Describe(code));
    }
}
