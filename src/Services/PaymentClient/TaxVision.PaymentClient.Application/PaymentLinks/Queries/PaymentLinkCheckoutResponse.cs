namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

/// <summary>Todo lo que la página de checkout pública necesita para renderizar el monto y
/// arrancar Stripe.js — <see cref="PublishableKey"/> es seguro de exponer (está diseñado para
/// vivir en el cliente), nunca el secret key.</summary>
public sealed record PaymentLinkCheckoutResponse(
    long AmountCents,
    string Currency,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string TenantName,
    string StatementDescriptor,
    string PublishableKey
);
