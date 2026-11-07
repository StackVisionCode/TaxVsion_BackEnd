namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Fallo pasajero de un reembolso por mensaje (proveedor caído, otro reembolso en curso). No es permanente:
/// la política estándar lo reintenta y, agotados los intentos, termina en la cola de errores.</summary>
public sealed class SaaSPaymentRefundRetryableException(string message) : Exception(message);
