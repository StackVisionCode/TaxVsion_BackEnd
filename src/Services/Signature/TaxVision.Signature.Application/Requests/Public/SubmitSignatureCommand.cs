using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// El firmante público envía su firma. <paramref name="Method"/> indica cómo la
/// capturó:
/// <list type="bullet">
///   <item><see cref="SignatureCaptureMethod.Typed"/> ⇒ <paramref name="TypedName"/> es
///     obligatorio y debe coincidir con el fullName del signer.</item>
///   <item><see cref="SignatureCaptureMethod.Drawn"/>/<see cref="SignatureCaptureMethod.Uploaded"/>
///     ⇒ <paramref name="SignatureImageFileId"/> es obligatorio. El frontend sube el PNG a
///     CloudStorage antes de invocar este comando y pasa el FileId.</item>
/// </list>
/// </summary>
public sealed record SubmitSignatureCommand(
    string Token,
    SignatureCaptureMethod Method,
    string? TypedName,
    Guid? SignatureImageFileId,
    string? ClientIp,
    string? UserAgent
);
