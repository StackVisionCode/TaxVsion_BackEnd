using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Infrastructure.PaymentApp;

/// <summary>Default stand-in — queda cableado para que un follow-up lo cambie por un cliente HTTP real contra
/// PaymentApp (ver IPaymentProvider.CancelHostedCheckoutSessionAsync pendiente). El guard local ya cambió la
/// intención a Cancelled; la sesión hosted en Stripe/PayPal expira sola en minutos.</summary>
internal sealed class NoOpHostedCheckoutCancellationClient(ILogger<NoOpHostedCheckoutCancellationClient> logger)
    : IHostedCheckoutCancellationClient
{
    public Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default)
    {
        logger.LogInformation(
            "No-op hosted checkout cancellation for tenant {TenantId}, payment {PaymentId}: local intent already Cancelled; provider session will expire on its own.",
            tenantId,
            saaSPaymentId
        );
        return Task.FromResult(Result.Success());
    }
}
