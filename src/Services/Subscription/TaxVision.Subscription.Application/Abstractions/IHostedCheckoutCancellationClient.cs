using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Best-effort cancel de una sesión de checkout hosteada en el proveedor (Stripe/PayPal) tras una
/// cancelación interactiva del usuario. Nunca bloquea el flujo local: si falla, el checkout del proveedor
/// expira por su cuenta y el guard local ya movió la intención a Cancelled.</summary>
public interface IHostedCheckoutCancellationClient
{
    /// <summary>Intenta expirar/cancelar la sesión hosted en PaymentApp. Siempre devuelve Success para no
    /// bloquear la UX del usuario; errores se loguean internamente.</summary>
    Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default);
}
