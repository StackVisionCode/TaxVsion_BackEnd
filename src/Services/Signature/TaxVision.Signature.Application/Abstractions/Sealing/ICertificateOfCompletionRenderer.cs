namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Renders the immutable legal evidence artifact for a completed request.</summary>
public interface ICertificateOfCompletionRenderer
{
    CertificateResult Render(CertificateOfCompletionModel model);
}
