using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Checkout de PaymentApp falso: cuenta las llamadas y devuelve una sesión fija.</summary>
public sealed class FakeWalletTopUpCheckoutClient : IWalletTopUpCheckoutClient
{
    public int Calls { get; private set; }
    public List<WalletTopUpCheckoutClientRequest> Requests { get; } = [];

    /// <summary>Si tiene valor, la próxima sesión falla con este error.</summary>
    public Error? NextFailure { get; set; }

    public Task<Result<WalletTopUpCheckoutClientResult>> CreateCheckoutAsync(
        WalletTopUpCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        Calls++;
        Requests.Add(request);
        if (NextFailure is { } failure)
            return Task.FromResult(Result.Failure<WalletTopUpCheckoutClientResult>(failure));

        return Task.FromResult(
            Result.Success(
                new WalletTopUpCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://checkout.test/session",
                    "cs_test",
                    DateTime.UtcNow.AddMinutes(30)
                )
            )
        );
    }
}
