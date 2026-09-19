namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// El firmante externo sube el PNG de su firma (dibujada en el pad, tecleada y rasterizada,
/// o subida como archivo) ANTES de invocar <c>Sign</c>. Devuelve el <c>FileId</c> con el que
/// luego referencia la evidencia en <see cref="SubmitSignatureCommand"/>. Autorizado sólo por
/// el token firmado del firmante — sin JWT.
/// </summary>
public sealed record AttachSignatureImageCommand(string Token, byte[] Content, string? ClientIp, string? UserAgent);
