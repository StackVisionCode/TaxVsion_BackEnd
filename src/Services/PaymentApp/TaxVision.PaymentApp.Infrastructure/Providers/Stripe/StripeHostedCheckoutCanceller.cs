using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using CheckoutSession = Stripe.Checkout.Session;
using CheckoutSessionService = Stripe.Checkout.SessionService;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>Expira una Checkout Session en Stripe (<c>POST /v1/checkout/sessions/{id}/expire</c>). Fuera de
/// <see cref="StripePaymentAdapter"/> para no obligar a <see cref="IPaymentProvider"/> a cargar con este método
/// que no todos los providers soportan (PayPal no tiene equivalente).</summary>
public sealed class StripeHostedCheckoutCanceller
{
    private readonly ILogger<StripeHostedCheckoutCanceller> _logger;
    private readonly Func<string, CancellationToken, Task<CheckoutSession>> _expire;

    public StripeHostedCheckoutCanceller(IOptions<StripeOptions> options, ILogger<StripeHostedCheckoutCanceller> logger)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new CheckoutSessionService(client);
        _logger = logger;
        _expire = (id, ct) => service.ExpireAsync(id, cancellationToken: ct);
    }

    // Seam de tests: permite inyectar un fake sin exponer el SDK ni tocar DI de producción.
    public StripeHostedCheckoutCanceller(
        Func<string, CancellationToken, Task<CheckoutSession>> expire,
        ILogger<StripeHostedCheckoutCanceller> logger
    )
    {
        _expire = expire;
        _logger = logger;
    }

    public async Task<Result<CancelHostedCheckoutResult>> CancelAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await _expire(sessionId, ct);
            return Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: false));
        }
        catch (StripeException ex) when (IsAlreadyTerminal(ex))
        {
            _logger.LogInformation(
                "Stripe reported session {SessionId} as already terminal ({Code}); treated as idempotent cancel.",
                sessionId,
                ex.StripeError?.Code
            );
            return Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: true));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe expire failed for session {SessionId}.", sessionId);
            return Result.Failure<CancelHostedCheckoutResult>(
                new Error("Stripe.CancelFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    private static bool IsAlreadyTerminal(StripeException ex)
    {
        var code = ex.StripeError?.Code;
        if (!string.IsNullOrEmpty(code))
        {
            if (
                string.Equals(code, "session_already_expired", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "session_completed", StringComparison.OrdinalIgnoreCase)
            )
                return true;
        }

        // Stripe no siempre pone un code estable para "ya expiró / ya completó"; el mensaje sí.
        var message = ex.StripeError?.Message ?? ex.Message ?? string.Empty;
        return message.Contains("already expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already completed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot be expired", StringComparison.OrdinalIgnoreCase);
    }
}
