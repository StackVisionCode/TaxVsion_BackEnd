namespace TaxVision.Signature.Domain.Requests;

/// <summary>Defines whether completion evidence is bundled or emitted per document.</summary>
public enum CertificateGenerationMode
{
    SingleForRequest = 0,
    PerDocument = 1,
}
