using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Respuesta binaria del TSA — token DER de <c>id-aa-timeStampToken</c>.</summary>
public sealed record TimestampToken(byte[] TokenDer, DateTime GenTimeUtc);

/// <summary>
/// Cliente RFC 3161 hacia un Timestamping Authority (TSA). Envía un TimeStampReq con
/// el digest del CMS SignerInfo.signature y recibe un TimeStampResp que contiene el
/// <c>id-aa-timeStampToken</c> firmable como <c>unsignedAttribute</c> del CMS.
///
/// <para>
/// Con esto el CMS pasa de PAdES-B a PAdES-B-T (Baseline + Timestamp), que Adobe Reader
/// valida como "firma con estampado temporal" con validez legal en jurisdicciones eIDAS
/// y IRS §7216.
/// </para>
/// </summary>
public interface ITimestampAuthorityClient
{
    Task<Result<TimestampToken>> RequestTimestampAsync(byte[] messageDigest, CancellationToken ct = default);
}
