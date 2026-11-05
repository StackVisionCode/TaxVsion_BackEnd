using TaxVision.Wallet.Application.Wallet.Commands;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Application;

public sealed class TopUpWalletHandlerTests
{
    [Fact]
    public async Task Handle_MxnCurrency_DoesNotCreateOrderNorCallPaymentApp()
    {
        var wallets = new FakeWalletRepository();
        var checkout = new FakeWalletTopUpCheckoutClient();
        var unitOfWork = new FakeUnitOfWork();
        var command = new TopUpWalletCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            10_000,
            "MXN",
            "payer@test.local",
            "https://app.test/ok",
            "https://app.test/cancel"
        );

        var result = await TopUpWalletHandler.Handle(command, wallets, checkout, unitOfWork, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.CurrencyMismatch, result.Error);
        Assert.Empty(wallets.TopUps);
        Assert.Equal(0, checkout.Calls);
        Assert.Equal(0, unitOfWork.SaveCount);
    }
}
