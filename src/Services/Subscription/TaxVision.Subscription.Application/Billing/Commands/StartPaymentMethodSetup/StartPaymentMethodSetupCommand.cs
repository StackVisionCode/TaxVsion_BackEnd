namespace TaxVision.Subscription.Application.Billing.Commands.StartPaymentMethodSetup;

/// <summary>Arranca una Checkout Session setup-only (sin cobro) para que el tenant actualice la tarjeta
/// guardada. Solo tiene sentido cuando la preferencia es Automatic — en Manual no hay método en archivo
/// que reemplazar.</summary>
public sealed record StartPaymentMethodSetupCommand(
    Guid TenantId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    Guid RequestedByUserId
);

public sealed record StartPaymentMethodSetupResponse(string CheckoutUrl, DateTime ExpiresAtUtc);
