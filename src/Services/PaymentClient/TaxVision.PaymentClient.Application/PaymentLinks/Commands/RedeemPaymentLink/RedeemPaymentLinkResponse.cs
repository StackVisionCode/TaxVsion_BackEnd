namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary><paramref name="ClientSecret"/>: solo en RequiresAction de Stripe, para el 3DS con Stripe.js en la
/// página de pago. No se guarda ni se escribe en logs.</summary>
public sealed record RedeemPaymentLinkResponse(
    Guid TenantPaymentId,
    string Status,
    string? NextActionType,
    string? NextActionUrl,
    string? FailureCode,
    string? FailureMessage,
    string? ClientSecret = null
)
{
    // El secreto no sale en ToString (logs).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"TenantPaymentId = {TenantPaymentId}, Status = {Status}");
        return true;
    }
}
