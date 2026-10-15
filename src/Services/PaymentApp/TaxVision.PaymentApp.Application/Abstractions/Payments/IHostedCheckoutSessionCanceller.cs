using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>Best-effort cancel/expire de una Checkout Session hosteada del provider. Separado de
/// <see cref="IPaymentProvider"/> para no obligar a cada adapter nuevo a implementarlo cuando el provider no
/// expone un endpoint real de cancelación (PayPal, Manual): esos devuelven <c>Success(alreadyExpired: false)</c>
/// y se apoyan en la expiración natural de la sesión.</summary>
public interface IHostedCheckoutSessionCanceller
{
    Task<Result<CancelHostedCheckoutResult>> CancelAsync(
        PaymentProviderCode providerCode,
        string providerSessionId,
        CancellationToken ct = default
    );
}

/// <summary><c>AlreadyExpired</c> es true cuando el provider devolvió que la sesión ya estaba expirada/completada
/// — idempotente desde la perspectiva del caller.</summary>
public sealed record CancelHostedCheckoutResult(bool AlreadyExpired);
