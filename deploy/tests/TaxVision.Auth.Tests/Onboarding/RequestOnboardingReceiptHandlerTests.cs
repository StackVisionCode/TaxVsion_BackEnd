using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>Re-cableado del recibo de onboarding — el handler mapea el comando al request de Documents,
/// enmascara la referencia del proveedor (últimos 4), fija la Idempotency-Key por OnboardingId y lanza
/// en fallo para que Wolverine reintente.</summary>
public sealed class RequestOnboardingReceiptHandlerTests
{
    private static RequestOnboardingReceiptCommand Command(
        Guid onboardingId,
        string? providerReference,
        long pricePaidCents = 4900
    ) =>
        new(
            onboardingId,
            "Ada",
            "Lovelace",
            "buyer@example.com",
            "Enterprise",
            pricePaidCents,
            "USD",
            DateTime.UtcNow,
            providerReference,
            "Visa •••• 4242",
            "corr-1"
        );

    [Fact]
    public async Task Maps_command_and_masks_provider_reference_to_last_four()
    {
        var client = new FakeReceiptDocumentClient(Result.Success());
        var onboardingId = Guid.NewGuid();

        await RequestOnboardingReceiptHandler.Handle(
            Command(onboardingId, "pi_test_ABCD"),
            client,
            NullLogger<RequestOnboardingReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(client.LastRequest);
        var req = client.LastRequest!;
        Assert.Equal(onboardingId, req.OnboardingId);
        Assert.Equal("Ada", req.PayerFirstName);
        Assert.Equal("Lovelace", req.PayerLastName);
        Assert.Equal("Enterprise", req.PlanName);
        Assert.Equal(4900, req.PricePaidCents);
        Assert.Equal("USD", req.Currency);
        Assert.Equal("ABCD", req.TransactionReferenceMask);
        Assert.Equal("Visa •••• 4242", req.PaymentMethodMasked);
        Assert.Equal($"onb-receipt:{onboardingId:N}", req.IdempotencyKey);
    }

    [Fact]
    public async Task Uses_empty_mask_when_there_is_no_provider_reference()
    {
        var client = new FakeReceiptDocumentClient(Result.Success());
        var onboardingId = Guid.NewGuid();

        // Carril $0 (cubierto 100% por código): sin pago, sin referencia, monto 0.
        await RequestOnboardingReceiptHandler.Handle(
            Command(onboardingId, providerReference: null, pricePaidCents: 0),
            client,
            NullLogger<RequestOnboardingReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(client.LastRequest);
        Assert.Equal(string.Empty, client.LastRequest!.TransactionReferenceMask);
        Assert.Equal(0, client.LastRequest!.PricePaidCents);
    }

    [Fact]
    public async Task Throws_when_documents_request_fails_so_wolverine_retries()
    {
        var client = new FakeReceiptDocumentClient(
            Result.Failure(new Error("ReceiptDocumentClient.RequestFailed", "Could not reach Documents."))
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RequestOnboardingReceiptHandler.Handle(
                Command(Guid.NewGuid(), "pi_test_ABCD"),
                client,
                NullLogger<RequestOnboardingReceiptCommand>.Instance,
                CancellationToken.None
            )
        );
    }
}
