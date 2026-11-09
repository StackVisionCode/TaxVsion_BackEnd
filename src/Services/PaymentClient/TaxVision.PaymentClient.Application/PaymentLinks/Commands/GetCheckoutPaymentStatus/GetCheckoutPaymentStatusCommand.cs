namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.GetCheckoutPaymentStatus;

/// <summary>
/// Estado del intento de pago de un link, pedido por la página pública al volver del 3DS. El token del link
/// es la prueba de posesión y solo se acepta el intento actual del link. Si el pago sigue esperando al
/// proveedor, se le pregunta en el momento (mismo camino que la reconciliación).
/// </summary>
public sealed record GetCheckoutPaymentStatusCommand(string LinkToken, Guid TenantPaymentId);

/// <summary>Respuesta pública: solo el estado, nada del pagador ni del proveedor.</summary>
public sealed record CheckoutPaymentStatusResponse(string Status);
