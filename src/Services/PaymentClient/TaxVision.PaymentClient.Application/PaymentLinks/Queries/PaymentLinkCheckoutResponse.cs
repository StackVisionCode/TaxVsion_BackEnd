namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

/// <summary>Todo lo que la página de checkout pública necesita: el monto/propósito y la lista de
/// métodos de pago que el tenant tiene ACTIVOS (multi-proveedor). Ya no asume Stripe: el taxpayer
/// elige uno de <see cref="Methods"/>. Puede venir vacía si el tenant no configuró ninguno.</summary>
public sealed record PaymentLinkCheckoutResponse(
    long AmountCents,
    string Currency,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string TenantName,
    IReadOnlyList<CheckoutPaymentMethod> Methods
);

/// <summary>Un método de pago ofrecible en el checkout. <see cref="PublishableKey"/> es seguro de
/// exponer (diseñado para el cliente, p. ej. arrancar Stripe.js); el secret key nunca sale.</summary>
public sealed record CheckoutPaymentMethod(
    string ProviderCode,
    string DisplayName,
    string StatementDescriptor,
    string PublishableKey
);
