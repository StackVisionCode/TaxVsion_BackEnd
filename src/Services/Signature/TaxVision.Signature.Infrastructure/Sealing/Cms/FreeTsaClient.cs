using System.Net.Http.Headers;
using System.Security.Cryptography;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cmp;
using Org.BouncyCastle.Asn1.Tsp;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tsp;
using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Infrastructure.Sealing.Cms;

public sealed class TsaClientOptions
{
    public const string SectionName = "Signature:Sealing:Tsa";

    /// <summary>URL del TSA compatible RFC 3161. Default: FreeTSA (dev/testing).</summary>
    public string Endpoint { get; set; } = "https://freetsa.org/tsr";

    /// <summary>Pedir cert incluido en la respuesta (recomendado para LTV).</summary>
    public bool RequestCertificate { get; set; } = true;

    /// <summary>OID de policy opcional del TSA (algunos requieren uno específico).</summary>
    public string? PolicyOid { get; set; }
}

/// <summary>
/// Cliente RFC 3161 con BouncyCastle + HttpClient. Envía <c>TimeStampReq</c> como
/// <c>application/timestamp-query</c> y parsea <c>TimeStampResp</c>.
///
/// <para>
/// La respuesta se guarda como blob DER — Signature lo embebe en el CMS como
/// <c>unsignedAttribute</c> (id-aa-timeStampToken, OID <c>1.2.840.113549.1.9.16.2.14</c>).
/// </para>
/// </summary>
public sealed class FreeTsaClient(
    HttpClient httpClient,
    IOptions<TsaClientOptions> options,
    ILogger<FreeTsaClient> logger
) : ITimestampAuthorityClient
{
    private const string TimestampRequestMediaType = "application/timestamp-query";
    private const string TimestampResponseMediaType = "application/timestamp-reply";

    public async Task<Result<TimestampToken>> RequestTimestampAsync(
        byte[] messageDigest,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(messageDigest);
        if (messageDigest.Length != 32)
            return Result.Failure<TimestampToken>(
                new Error("Signature.Tsa.DigestSize", "Message digest must be 32 bytes (SHA-256).")
            );

        var opt = options.Value;
        var requestBytes = BuildTimeStampRequest(messageDigest, opt);

        using var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(TimestampRequestMediaType);
        using var response = await httpClient.PostAsync(opt.Endpoint, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TSA {Endpoint} returned HTTP {Status}.", opt.Endpoint, (int)response.StatusCode);
            return Result.Failure<TimestampToken>(
                new Error("Signature.Tsa.Http", $"TSA HTTP status {(int)response.StatusCode}.")
            );
        }
        if (response.Content.Headers.ContentType?.MediaType != TimestampResponseMediaType)
            logger.LogInformation(
                "TSA returned unexpected content type {ContentType}; parsing anyway.",
                response.Content.Headers.ContentType
            );

        var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
        return ParseTimestampResponse(responseBytes);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una responsabilidad
    // ------------------------------------------------------------------

    private static byte[] BuildTimeStampRequest(byte[] messageDigest, TsaClientOptions opt)
    {
        var nonce = new BigInteger(1, RandomNumberGenerator.GetBytes(16));
        var generator = new TimeStampRequestGenerator();
        if (opt.RequestCertificate)
            generator.SetCertReq(true);
        if (!string.IsNullOrWhiteSpace(opt.PolicyOid))
            generator.SetReqPolicy(opt.PolicyOid);

        var request = generator.Generate(TspAlgorithms.Sha256, messageDigest, nonce);
        return request.GetEncoded();
    }

    private static Result<TimestampToken> ParseTimestampResponse(byte[] responseBytes)
    {
        try
        {
            var response = new TimeStampResponse(responseBytes);
            var statusCode = response.Status;
            if (statusCode != (int)PkiStatus.Granted && statusCode != (int)PkiStatus.GrantedWithMods)
                return Result.Failure<TimestampToken>(
                    new Error("Signature.Tsa.Status", $"TSA rejected the request (status={statusCode}).")
                );

            var token = response.TimeStampToken;
            var tokenBytes = token.GetEncoded();
            var genTime = token.TimeStampInfo.GenTime;
            return Result.Success(new TimestampToken(tokenBytes, genTime));
        }
        catch (Exception ex) when (ex is TspException or IOException or InvalidOperationException)
        {
            return Result.Failure<TimestampToken>(
                new Error("Signature.Tsa.Parse", $"TSA response could not be parsed: {ex.Message}")
            );
        }
    }
}
