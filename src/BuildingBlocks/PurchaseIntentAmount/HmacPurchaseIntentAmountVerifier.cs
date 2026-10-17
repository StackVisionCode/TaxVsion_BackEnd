using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — verifica HMAC-SHA256 contra <see cref="PurchaseIntentAmountSignatureOptions.Secret"/>
/// y, como fallback para rotación, contra <see cref="PurchaseIntentAmountSignatureOptions.PreviousSecret"/>.
/// Clock skew: la firma expira pasado <see cref="PurchaseIntentAmountSignatureOptions.ClockSkew"/> desde
/// <c>signedAt</c>. Comparación con <see cref="CryptographicOperations.FixedTimeEquals"/> para no filtrar
/// tiempo. Backward-compat: si <c>signature</c> está vacía y <c>RejectUnsigned=false</c>, pass-through
/// con warning — permite rolling deploy donde el verifier sube antes que los signers.</summary>
public sealed class HmacPurchaseIntentAmountVerifier(
    IOptionsMonitor<PurchaseIntentAmountSignatureOptions> options,
    TimeProvider timeProvider,
    ILogger<HmacPurchaseIntentAmountVerifier> logger
) : IPurchaseIntentAmountVerifier
{
    public Result Verify(PurchaseIntentAmountInput input, string? signature)
    {
        var opt = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(signature))
        {
            if (opt.RejectUnsigned)
                return Result.Failure(
                    new Error(
                        "PurchaseIntent.AmountSignature.Missing",
                        "Request did not include an amount signature and the verifier is in strict mode."
                    )
                );
            logger.LogWarning(
                "Unsigned purchase intent amount accepted (RejectUnsigned=false) for {Kind} {IntentId} tenant {TenantId}.",
                input.Kind,
                input.IntentId,
                input.TenantId
            );
            return Result.Success();
        }

        var parts = signature.Split('.', 3);
        if (parts.Length != 3)
            return Result.Failure(
                new Error(
                    "PurchaseIntent.AmountSignature.Malformed",
                    "Signature must have 3 dot-separated parts: version, mac, signedAt."
                )
            );

        var (version, mac, signedAtRaw) = (parts[0], parts[1], parts[2]);

        if (!string.Equals(version, PurchaseIntentAmountCanonicalBuilder.Version, StringComparison.Ordinal))
            return Result.Failure(
                new Error(
                    "PurchaseIntent.AmountSignature.VersionUnsupported",
                    $"Signature version '{version}' is not supported by this verifier."
                )
            );

        if (!long.TryParse(signedAtRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedAt))
            return Result.Failure(
                new Error("PurchaseIntent.AmountSignature.Malformed", "signedAt is not a valid Unix seconds integer.")
            );

        var nowUnix = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var skewSeconds = (long)opt.ClockSkew.TotalSeconds;
        if (Math.Abs(nowUnix - signedAt) > skewSeconds)
            return Result.Failure(
                new Error(
                    "PurchaseIntent.AmountSignature.Expired",
                    $"Signature signedAt is outside the clock skew window of {skewSeconds}s."
                )
            );

        byte[] providedMac;
        try
        {
            providedMac = Convert.FromBase64String(mac);
        }
        catch (FormatException)
        {
            return Result.Failure(
                new Error("PurchaseIntent.AmountSignature.Malformed", "Signature mac is not valid Base64.")
            );
        }

        var canonical = PurchaseIntentAmountCanonicalBuilder.Build(input, signedAt);

        if (TryMatch(opt.Secret, canonical, providedMac))
            return Result.Success();
        if (!string.IsNullOrWhiteSpace(opt.PreviousSecret) && TryMatch(opt.PreviousSecret!, canonical, providedMac))
        {
            logger.LogInformation(
                "Purchase intent amount signature verified against PreviousSecret for {Kind} {IntentId} — rotate soon.",
                input.Kind,
                input.IntentId
            );
            return Result.Success();
        }

        logger.LogWarning(
            "Purchase intent amount signature mismatch for {Kind} {IntentId} tenant {TenantId}; "
                + "requested {AmountCents} {Currency}.",
            input.Kind,
            input.IntentId,
            input.TenantId,
            input.AmountCents,
            input.Currency
        );
        return Result.Failure(
            new Error(
                "PurchaseIntent.AmountSignature.Invalid",
                "Amount signature does not match. The amount/currency/intentId in the request was likely tampered."
            )
        );
    }

    private static bool TryMatch(string secret, string canonical, byte[] providedMac)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return CryptographicOperations.FixedTimeEquals(expected, providedMac);
    }
}
