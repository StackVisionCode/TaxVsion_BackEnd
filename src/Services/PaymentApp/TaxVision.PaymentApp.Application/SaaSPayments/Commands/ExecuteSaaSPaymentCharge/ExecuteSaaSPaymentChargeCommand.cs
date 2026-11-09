namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;

/// <summary>Cómo se cobra: <c>Authorize</c> va por el adaptador del provider, <c>OffSession</c> por el charger.</summary>
public enum SaaSPaymentChargeStrategy
{
    Authorize = 1,
    OffSession = 2,
}

/// <summary>
/// Llama al provider por un pago ya guardado en <c>Pending</c>. Sale en la misma transacción que la fila, a la cola
/// durable <c>payment-app-provider-calls</c>. <paramref name="MethodReference"/> y
/// <paramref name="CustomerReference"/> fijan lo que se cobra: un reintento manda lo mismo y Stripe acepta la clave.
/// </summary>
public sealed record ExecuteSaaSPaymentChargeCommand(
    Guid TenantId,
    Guid SaaSPaymentId,
    SaaSPaymentChargeStrategy Strategy,
    string MethodReference,
    string? CustomerReference,
    Guid ActorUserId,
    string? CorrelationId
);
