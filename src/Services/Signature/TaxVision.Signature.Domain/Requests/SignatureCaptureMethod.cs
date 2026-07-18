namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Cómo se capturó la firma del signer al momento del <c>MarkSignerSigned</c>. Se
/// registra en el <c>Signer</c> como evidencia legal — cada método tiene requisitos:
/// <list type="bullet">
///   <item><see cref="Typed"/>: el firmante escribió su nombre; se guarda el string.</item>
///   <item><see cref="Drawn"/>: el firmante dibujó la firma en canvas; el frontend
///     subió el PNG a CloudStorage y guardamos el <c>FileId</c>.</item>
///   <item><see cref="Uploaded"/>: el firmante subió una imagen escaneada; también
///     como <c>FileId</c> de CloudStorage.</item>
/// </list>
/// </summary>
public enum SignatureCaptureMethod
{
    Typed,
    Drawn,
    Uploaded,
}
