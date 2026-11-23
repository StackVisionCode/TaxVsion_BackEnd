using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.AddOnCheckouts.Commands;
using TaxVision.PaymentApp.Application.PaymentMethodSetup.Commands;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Un checkout hosteado solo vuelve a una app de la plataforma: una URL de regreso ajena se rechaza
/// antes de crear el pago o hablar con el proveedor (redirección abierta después de pagar).
/// </summary>
public sealed class CheckoutReturnUrlTests
{
    private static CreateAddOnCheckoutCommand AddOnCommand(string successUrl, string cancelUrl)
    {
        var intentId = Guid.NewGuid();
        return new(
            Guid.NewGuid(),
            intentId,
            AmountCents: 2900,
            "USD",
            "buyer@example.com",
            successUrl,
            cancelUrl,
            $"addon-checkout-{intentId:N}"
        );
    }

    [Theory]
    [InlineData("https://evil.com/success", "https://app.taxoffix.io/cancel")]
    [InlineData("https://app.taxoffix.io/success", "https://evil.com/cancel")]
    [InlineData("https://app.taxoffix.io@evil.com/success", "https://app.taxoffix.io/cancel")]
    [InlineData("https://app.taxoffix.io.evil.com/success", "https://app.taxoffix.io/cancel")]
    [InlineData("http://app.taxoffix.io/success", "https://app.taxoffix.io/cancel")]
    [InlineData("javascript:alert(1)", "https://app.taxoffix.io/cancel")]
    public async Task Add_on_checkout_rejects_a_foreign_return_url_before_calling_the_provider(
        string successUrl,
        string cancelUrl
    )
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();

        var result = await CreateAddOnCheckoutHandler.Handle(
            AddOnCommand(successUrl, cancelUrl),
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            new FakePurchaseIntentAmountVerifier(),
            NullLogger<SaaSPayment>.Instance,
            TestPlatform.Settings,
            TestPlatform.ReturnUrls,
            FakePaymentMethodCatalog.AllEnabled(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.InvalidReturnUrl", result.Error.Code);
        Assert.Null(payments.Added);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task Payment_method_setup_rejects_a_foreign_return_url_before_calling_the_provider()
    {
        var creator = new RecordingCreator();

        var result = await CreatePaymentMethodSetupSessionHandler.Handle(
            new CreatePaymentMethodSetupSessionCommand(
                Guid.NewGuid(),
                "buyer@example.com",
                "https://evil.com/success",
                "https://www.taxoffix.io/account/billing",
                "setup-key"
            ),
            new SingleCreatorFactory(creator),
            NullLogger<CreatePaymentMethodSetupSessionCommand>.Instance,
            TestPlatform.ReturnUrls,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.InvalidReturnUrl", result.Error.Code);
        Assert.Null(creator.LastRequest);
    }

    private sealed class RecordingCreator : IHostedCheckoutSessionCreator
    {
        public HostedCheckoutCreateRequest? LastRequest { get; private set; }

        public string ProviderCode => "Stripe";

        public Task<Result<HostedCheckoutSession>> CreateAsync(
            HostedCheckoutCreateRequest request,
            CancellationToken ct
        )
        {
            LastRequest = request;
            return Task.FromResult(
                Result.Success(new HostedCheckoutSession(Guid.Empty, "https://checkout.stripe.com/s", DateTime.UtcNow))
            );
        }
    }

    private sealed class SingleCreatorFactory(IHostedCheckoutSessionCreator creator)
        : IHostedCheckoutSessionCreatorFactory
    {
        public IHostedCheckoutSessionCreator? For(string? providerCode) => creator;
    }
}
