using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.HostedCheckout.Commands;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Expira la sesión del provider fuera de la transacción del consumidor; un fallo lanza para que haya reintento.</summary>
public sealed class ExpireProviderCheckoutSessionHandlerTests
{
    [Fact]
    public async Task Expires_the_provider_session()
    {
        var canceller = new RecordingCanceller(Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: false)));

        await ExpireProviderCheckoutSessionHandler.Handle(
            new ExpireProviderCheckoutSessionCommand(Guid.NewGuid(), PaymentProviderCode.Stripe, "cs_live_session"),
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal((PaymentProviderCode.Stripe, "cs_live_session"), Assert.Single(canceller.Calls));
    }

    [Fact]
    public async Task Provider_failure_throws_so_the_command_is_retried()
    {
        var canceller = new RecordingCanceller(
            Result.Failure<CancelHostedCheckoutResult>(new Error("Stripe.CancelFailed", "Stripe is down."))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExpireProviderCheckoutSessionHandler.Handle(
                new ExpireProviderCheckoutSessionCommand(Guid.NewGuid(), PaymentProviderCode.Stripe, "cs_live_session"),
                canceller,
                NullLogger<SaaSPayment>.Instance,
                CancellationToken.None
            )
        );
    }

    private sealed class RecordingCanceller(Result<CancelHostedCheckoutResult> result) : IHostedCheckoutSessionCanceller
    {
        public List<(PaymentProviderCode Code, string SessionId)> Calls { get; } = [];

        public Task<Result<CancelHostedCheckoutResult>> CancelAsync(
            PaymentProviderCode providerCode,
            string providerSessionId,
            CancellationToken ct = default
        )
        {
            Calls.Add((providerCode, providerSessionId));
            return Task.FromResult(result);
        }
    }
}
