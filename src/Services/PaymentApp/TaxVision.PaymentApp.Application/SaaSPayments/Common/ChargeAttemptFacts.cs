using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Lo que el correo de "no pudimos cobrar" necesita saber de un intento fallido: cuál fue, cuánto, con
/// qué tarjeta y si era un cobro automático. Viaja en los eventos de fallo de renovación.
/// </summary>
public sealed record ChargeAttemptFacts(
    int AttemptNumber,
    int MaxAttempts,
    long AmountCents,
    string Currency,
    string? CardBrand,
    string? CardLast4,
    bool IsAutomaticCharge
)
{
    /// <summary>
    /// La tarjeta es la guardada por defecto del tenant en ese proveedor. Si no se puede leer, el evento
    /// sale igual sin ella: Subscription depende de ese evento para el dunning y el correo se arregla sin
    /// los últimos 4 ("your saved card").
    /// </summary>
    public static async Task<ChargeAttemptFacts> ResolveAsync(
        SaaSPayment payment,
        ITenantProviderCustomerRepository? customers,
        CancellationToken ct
    )
    {
        string? brand = null;
        string? last4 = null;
        if (customers is not null && payment.TenantId != Guid.Empty)
        {
            try
            {
                var customer = await customers.GetByTenantAndProviderAsync(payment.TenantId, payment.ProviderCode, ct);
                var card = customer?.GetDefaultMethod();
                brand = card?.Brand;
                last4 = card?.Last4;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin tarjeta en el correo es mejor que perder el evento de fallo.
            }
        }

        // El checkout hosteado deja su sesión: ahí el usuario paga en pantalla y reintenta él mismo.
        var isAutomaticCharge = payment.ProviderCheckoutSessionId is null;

        return new ChargeAttemptFacts(
            Math.Max(1, payment.Attempts.Count),
            SaaSPaymentChargeOutcome.MaxDunningAttempts,
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            brand,
            last4,
            isAutomaticCharge
        );
    }
}
