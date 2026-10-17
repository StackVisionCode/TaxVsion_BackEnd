using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — firma HMAC-SHA256. El signer siempre usa <see cref="PurchaseIntentAmountSignatureOptions.Secret"/>
/// (nunca el previous, para no quedarse atrás en una rotación). Si la secret no está configurada,
/// retorna <see cref="string.Empty"/> y log warn — el verifier del otro lado decide si rechaza con
/// <c>RejectUnsigned=true</c>; durante el deploy inicial las dos puntas pueden operar sin secret.</summary>
public sealed class HmacPurchaseIntentAmountSigner(
    IOptionsMonitor<PurchaseIntentAmountSignatureOptions> options,
    TimeProvider timeProvider,
    ILogger<HmacPurchaseIntentAmountSigner> logger
) : IPurchaseIntentAmountSigner
{
    public string Sign(PurchaseIntentAmountInput input)
    {
        var opt = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opt.Secret))
        {
            logger.LogWarning(
                "PurchaseIntentAmountSignature secret is not configured; emitting unsigned token. "
                    + "Caller must configure PurchaseIntentAmountSignature:Secret before the verifier's RejectUnsigned flag flips."
            );
            return string.Empty;
        }

        var signedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var canonical = PurchaseIntentAmountCanonicalBuilder.Build(input, signedAt);
        var mac = HmacSha256Base64(opt.Secret, canonical);
        return string.Concat(
            PurchaseIntentAmountCanonicalBuilder.Version,
            ".",
            mac,
            ".",
            signedAt.ToString(CultureInfo.InvariantCulture)
        );
    }

    internal static string HmacSha256Base64(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hash);
    }
}
