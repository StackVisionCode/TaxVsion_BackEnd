using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.AttachPaymentMethod;

/// <summary>El frontend ya tokenizó la tarjeta (Stripe Elements / SetupIntent) antes de
/// llegar acá — <see cref="PaymentMethodReference"/> es el <c>pm_xxx</c> resultante, nunca
/// datos crudos de tarjeta. El backend confirma brand/last4/expiración directo con el
/// provider, nunca confía en lo que el cliente afirma.</summary>
public sealed record AttachPaymentMethodCommand(
    Guid TenantId,
    PaymentProviderCode Provider,
    string PaymentMethodReference,
    bool SetAsDefault,
    Guid ActorUserId
);
