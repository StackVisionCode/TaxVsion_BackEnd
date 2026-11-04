namespace TaxVision.Signature.Domain.Requests;

/// <summary>Archivo final del sellado que una solicitud referencia por FileId.</summary>
public enum SealingArtifactKind
{
    SealedDocument = 0,
    RequestCertificate = 1,
    DocumentCertificate = 2,
}
