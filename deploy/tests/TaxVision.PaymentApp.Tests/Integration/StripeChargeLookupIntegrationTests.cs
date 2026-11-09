using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-5 contra el sandbox real de Stripe: la reconciliación encuentra el cargo por su metadata. Solo corre con
/// una clave <c>sk_test_</c>; con otra cosa no llama a Stripe.
/// </summary>
public sealed class StripeChargeLookupIntegrationTests
{
    private readonly string? _secretKey =
        Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY")
        ?? new ConfigurationBuilder().AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-paymentapp001").Build()[
            "Stripe:SecretKey"
        ];

    private bool SandboxAvailable => _secretKey?.StartsWith("sk_test_", StringComparison.Ordinal) == true;

    [Fact]
    public async Task ChargeMadeInStripe_IsFoundByItsMetadata()
    {
        if (!SandboxAvailable)
            return;

        var saaSPaymentId = Guid.NewGuid();
        var createdAfterUtc = DateTime.UtcNow.AddMinutes(-1);
        var intent = await new PaymentIntentService(new StripeClient(_secretKey)).CreateAsync(
            new PaymentIntentCreateOptions
            {
                Amount = 100,
                Currency = "usd",
                PaymentMethod = "pm_card_visa",
                PaymentMethodTypes = ["card"],
                Confirm = true,
                Metadata = new Dictionary<string, string> { ["saaSPaymentId"] = saaSPaymentId.ToString("N") },
            }
        );
        Assert.Equal("succeeded", intent.Status);

        // Search tarda hasta un minuto en ver un cargo nuevo.
        var adapter = NewAdapter();
        ChargeLookup? lookup = null;
        for (var attempt = 0; attempt < 24 && lookup?.Outcome != ChargeLookupOutcome.Found; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var result = await adapter.FindChargeAsync(saaSPaymentId, createdAfterUtc, CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            lookup = result.Value;
        }

        Assert.Equal(ChargeLookupOutcome.Found, lookup?.Outcome);
    }

    [Fact]
    public async Task PaymentWithoutCharge_IsNotFound()
    {
        if (!SandboxAvailable)
            return;

        var result = await NewAdapter().FindChargeAsync(Guid.NewGuid(), DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ChargeLookupOutcome.NotFound, result.Value.Outcome);
    }

    private StripePaymentAdapter NewAdapter() =>
        new(
            Options.Create(new StripeOptions { SecretKey = _secretKey!, WebhookSecret = "unused" }),
            NullLogger<StripePaymentAdapter>.Instance,
            new FakePaymentAppMetrics()
        );
}
