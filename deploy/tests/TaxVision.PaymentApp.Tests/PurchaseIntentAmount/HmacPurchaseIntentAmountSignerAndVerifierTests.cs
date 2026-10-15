using BuildingBlocks.PurchaseIntentAmount;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Tests.PurchaseIntentAmount;

/// <summary>SEC-M1.C — ida y vuelta signer→verifier, con el escenario de ataque (amount tampered)
/// y los dos huecos de rollout (sin firma + rotación de secret).</summary>
public sealed class HmacPurchaseIntentAmountSignerAndVerifierTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Intent = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Secret = "d2VyeS1zZWNyZXQtMzItYnl0ZXMtcmFuZG9tLWtleS1oZXJlIQ==";
    private const string OtherSecret = "YW5vdGhlci1zZWNyZXQtMzItYnl0ZXMtcmFuZG9tLWtleS0h";

    private static PurchaseIntentAmountInput Input(long amount = 29900, string currency = "USD") =>
        new(Tenant, PurchaseIntentKind.PlanChange, Intent, amount, currency);

    [Fact]
    public void Signer_and_verifier_round_trip_matches()
    {
        var (signer, verifier, _) = Build(Secret);
        var input = Input();

        var signature = signer.Sign(input);
        var result = verifier.Verify(input, signature);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verifier_rejects_tampered_amount()
    {
        var (signer, verifier, _) = Build(Secret);
        var signature = signer.Sign(Input(amount: 29900));

        var result = verifier.Verify(Input(amount: 100), signature);

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.Invalid", result.Error.Code);
    }

    [Fact]
    public void Verifier_rejects_tampered_currency()
    {
        var (signer, verifier, _) = Build(Secret);
        var signature = signer.Sign(Input(currency: "USD"));

        var result = verifier.Verify(Input(currency: "EUR"), signature);

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.Invalid", result.Error.Code);
    }

    [Fact]
    public void Verifier_normalises_currency_case()
    {
        // "usd" firmado debe validar contra "USD" verificado — ambos lados pasan por ToUpperInvariant.
        var (signer, verifier, _) = Build(Secret);
        var signature = signer.Sign(Input(currency: "usd"));

        var result = verifier.Verify(Input(currency: "USD"), signature);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verifier_rejects_signature_outside_clock_skew()
    {
        // Firma hecha hace 11 min; verifier con ClockSkew=10m la rechaza.
        var frozenNow = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var signerClock = new FakeTimeProvider(frozenNow.AddMinutes(-11));
        var verifierClock = new FakeTimeProvider(frozenNow);
        var options = Options.Create(
            new PurchaseIntentAmountSignatureOptions { Secret = Secret, ClockSkew = TimeSpan.FromMinutes(10) }
        );

        var signer = new HmacPurchaseIntentAmountSigner(
            new StaticOptionsMonitor<PurchaseIntentAmountSignatureOptions>(options.Value),
            signerClock,
            NullLogger<HmacPurchaseIntentAmountSigner>.Instance
        );
        var verifier = new HmacPurchaseIntentAmountVerifier(
            new StaticOptionsMonitor<PurchaseIntentAmountSignatureOptions>(options.Value),
            verifierClock,
            NullLogger<HmacPurchaseIntentAmountVerifier>.Instance
        );
        var signature = signer.Sign(Input());

        var result = verifier.Verify(Input(), signature);

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.Expired", result.Error.Code);
    }

    [Fact]
    public void Verifier_accepts_signature_from_previous_secret_during_rotation()
    {
        // Rotación: signer antiguo firmó con la secret "anterior"; verifier nuevo tiene secret
        // nueva pero conserva previous. Debe aceptar.
        var oldOptions = Options.Create(new PurchaseIntentAmountSignatureOptions { Secret = OtherSecret });
        var newOptions = Options.Create(
            new PurchaseIntentAmountSignatureOptions { Secret = Secret, PreviousSecret = OtherSecret }
        );

        var signer = new HmacPurchaseIntentAmountSigner(
            new StaticOptionsMonitor<PurchaseIntentAmountSignatureOptions>(oldOptions.Value),
            TimeProvider.System,
            NullLogger<HmacPurchaseIntentAmountSigner>.Instance
        );
        var verifier = new HmacPurchaseIntentAmountVerifier(
            new StaticOptionsMonitor<PurchaseIntentAmountSignatureOptions>(newOptions.Value),
            TimeProvider.System,
            NullLogger<HmacPurchaseIntentAmountVerifier>.Instance
        );

        var signature = signer.Sign(Input());
        var result = verifier.Verify(Input(), signature);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verifier_passes_through_unsigned_when_RejectUnsigned_false()
    {
        var (_, verifier, _) = Build(Secret, rejectUnsigned: false);

        var result = verifier.Verify(Input(), signature: null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verifier_rejects_unsigned_when_RejectUnsigned_true()
    {
        var (_, verifier, _) = Build(Secret, rejectUnsigned: true);

        var result = verifier.Verify(Input(), signature: "");

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.Missing", result.Error.Code);
    }

    [Fact]
    public void Verifier_rejects_malformed_signature_structure()
    {
        var (_, verifier, _) = Build(Secret);

        var result = verifier.Verify(Input(), "not-three-parts");

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.Malformed", result.Error.Code);
    }

    [Fact]
    public void Verifier_rejects_version_mismatch()
    {
        var (_, verifier, _) = Build(Secret);

        var result = verifier.Verify(Input(), "v99.AAAA.1700000000");

        Assert.True(result.IsFailure);
        Assert.Equal("PurchaseIntent.AmountSignature.VersionUnsupported", result.Error.Code);
    }

    [Fact]
    public void Signer_without_secret_emits_empty_token()
    {
        var (signer, _, _) = Build(secret: "");

        var signature = signer.Sign(Input());

        Assert.Equal(string.Empty, signature);
    }

    private static (
        HmacPurchaseIntentAmountSigner signer,
        HmacPurchaseIntentAmountVerifier verifier,
        PurchaseIntentAmountSignatureOptions options
    ) Build(string secret, bool rejectUnsigned = false)
    {
        var options = new PurchaseIntentAmountSignatureOptions { Secret = secret, RejectUnsigned = rejectUnsigned };
        var monitor = new StaticOptionsMonitor<PurchaseIntentAmountSignatureOptions>(options);
        var signer = new HmacPurchaseIntentAmountSigner(
            monitor,
            TimeProvider.System,
            NullLogger<HmacPurchaseIntentAmountSigner>.Instance
        );
        var verifier = new HmacPurchaseIntentAmountVerifier(
            monitor,
            TimeProvider.System,
            NullLogger<HmacPurchaseIntentAmountVerifier>.Instance
        );
        return (signer, verifier, options);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
