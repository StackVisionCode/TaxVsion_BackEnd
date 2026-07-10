using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Obtiene los CRLs asociados a un certificado leyendo la extension
/// <c>CRL Distribution Points</c>. Cachea el DER por dia en Redis (via
/// <c>IDistributedCache</c>) para no re-fetchear el mismo CRL en cada firma.
/// Sin cache disponible cae a HTTP en vivo.
/// </summary>
public sealed class CrlFetcher(HttpClient httpClient, IDistributedCache cache, ILogger<CrlFetcher> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<byte[]>> FetchAsync(X509Certificate certificate, CancellationToken ct)
    {
        var urls = ExtractCrlUrls(certificate);
        var result = new List<byte[]>(urls.Count);
        foreach (var url in urls)
        {
            var crl = await FetchOneAsync(url, ct);
            if (crl is not null)
                result.Add(crl);
        }
        return result;
    }

    private async Task<byte[]?> FetchOneAsync(string url, CancellationToken ct)
    {
        var cacheKey = $"pades:crl:{url}";
        try
        {
            var cached = await cache.GetAsync(cacheKey, ct);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "CRL cache read failed for {Url}", url);
        }

        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CRL {Url} returned HTTP {Status}", url, (int)response.StatusCode);
                return null;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            try
            {
                await cache.SetAsync(
                    cacheKey,
                    bytes,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                    ct
                );
            }
            catch (Exception cacheEx)
            {
                logger.LogDebug(cacheEx, "CRL cache write failed for {Url}", url);
            }
            return bytes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CRL {Url} fetch failed", url);
            return null;
        }
    }

    private static IReadOnlyList<string> ExtractCrlUrls(X509Certificate certificate)
    {
        var extBytes = certificate.GetExtensionValue(X509Extensions.CrlDistributionPoints);
        if (extBytes is null)
            return Array.Empty<string>();

        var octet = Asn1OctetString.GetInstance(extBytes.GetOctets());
        var distributionPoints = CrlDistPoint.GetInstance(Asn1Object.FromByteArray(octet.GetOctets()));
        var urls = new List<string>();
        foreach (var dp in distributionPoints.GetDistributionPoints())
        {
            if (dp.DistributionPointName?.Name is not GeneralNames names)
                continue;
            foreach (var name in names.GetNames())
            {
                if (name.TagNo != GeneralName.UniformResourceIdentifier)
                    continue;
                var uri = DerIA5String.GetInstance(name.Name).GetString();
                if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    urls.Add(uri);
            }
        }
        return urls;
    }
}
