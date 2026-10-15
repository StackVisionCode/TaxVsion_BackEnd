namespace TaxVision.PaymentApp.Application.PaymentMethodSetup.Commands;

/// <summary>Crea una Checkout Session en modo "setup" para capturar/reemplazar la tarjeta de un tenant,
/// SIN cobrar. El webhook <c>checkout.session.completed</c> con <c>mode=setup</c> adjunta el método y
/// reemplaza el default existente. No crea un <c>SaaSPayment</c> — no hay cargo que trackear.</summary>
public sealed record CreatePaymentMethodSetupSessionCommand(
    Guid TenantId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string? Provider = null
);

public sealed record PaymentMethodSetupSessionResponse(string CheckoutUrl, DateTime ExpiresAtUtc);
