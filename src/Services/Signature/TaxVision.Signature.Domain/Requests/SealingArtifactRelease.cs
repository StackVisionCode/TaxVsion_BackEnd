namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Resultado de soltar un archivo final rechazado por CloudStorage: qué se soltó y qué certificados, que
/// sí existen pero atestiguan un sellado que nunca existirá, quedaron desligados de la solicitud.
/// </summary>
public sealed record SealingArtifactRelease(SealingArtifactKind Kind, IReadOnlyList<Guid> DetachedCertificateFileIds);
