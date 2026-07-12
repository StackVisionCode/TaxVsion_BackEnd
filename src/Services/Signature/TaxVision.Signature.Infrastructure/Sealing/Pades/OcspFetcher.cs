using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.X509;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Consulta la URL OCSP publicada por un certificado (via extension
/// <c>Authority Info Access</c>) para obtener la respuesta que se embebe en el DSS
/// (<c>/OCSPs</c>). Nonce por peticion para evitar respuestas cacheadas por el
/// responder. Respuestas cachean 6h en Redis por <c>issuer + serial</c>.
/// </summary>
public sealed class OcspFetcher(HttpClient httpClient, IDistributedCache cache, ILogger<OcspFetcher> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private const string OcspRequestMediaType = "application/ocsp-request";

    public async Task<byte[]?> FetchAsync(X509Certificate certificate, X509Certificate issuer, CancellationToken ct)
    {
        var url = ExtractOcspUrl(certificate);
        if (url is null)
            return null;

        var cacheKey = $"pades:ocsp:{certificate.IssuerDN}:{certificate.SerialNumber}";
        try
        {
            var cached = await cache.GetAsync(cacheKey, ct);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OCSP cache read failed for {Url}", url);
        }

        var requestBytes = BuildOcspRequest(certificate, issuer);
        try
        {
            using var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(OcspRequestMediaType);
            using var response = await httpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OCSP {Url} returned HTTP {Status}", url, (int)response.StatusCode);
                return null;
            }
            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            try
            {
                await cache.SetAsync(
                    cacheKey,
                    responseBytes,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                    ct
                );
            }
            catch (Exception cacheEx)
            {
                logger.LogDebug(cacheEx, "OCSP cache write failed for {Url}", url);
            }
            return responseBytes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCSP {Url} fetch failed", url);
            return null;
        }
    }

    private static byte[] BuildOcspRequest(X509Certificate certificate, X509Certificate issuer)
    {
        var generator = new OcspReqGenerator();
        // CertificateID: BouncyCastle 2.x aun no expone reemplazo publico para OCSP CertID
        // en su API OCSP legacy; el warning es por deprecacion, no defecto funcional.
#pragma warning disable CS0618
        var certId = new CertificateID(OiwObjectIdentifiers.IdSha1.Id, issuer, certificate.SerialNumber);
#pragma warning restore CS0618
        generator.AddRequest(certId);
        return generator.Generate().GetEncoded();
    }

    private static string? ExtractOcspUrl(X509Certificate certificate)
    {
        var extBytes = certificate.GetExtensionValue(X509Extensions.AuthorityInfoAccess);
        if (extBytes is null)
            return null;
        var octet = Asn1OctetString.GetInstance(extBytes.GetOctets());
        var aia = AuthorityInformationAccess.GetInstance(Asn1Object.FromByteArray(octet.GetOctets()));
        foreach (var description in aia.GetAccessDescriptions())
        {
            if (description.AccessMethod.Equals(X509ObjectIdentifiers.OcspAccessMethod))
            {
                var name = description.AccessLocation;
                if (name.TagNo == GeneralName.UniformResourceIdentifier)
                    return DerIA5String.GetInstance(name.Name).GetString();
            }
        }
        return null;
    }
}
