using Org.BouncyCastle.X509;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Material de validacion recolectado por los fetchers para incrustar en el DSS
/// de PAdES-B-LT: cadena de certificados, CRLs, respuestas OCSP.
/// </summary>
public sealed class ValidationMaterial
{
    public List<X509Certificate> Certificates { get; } = new();
    public List<byte[]> Crls { get; } = new();
    public List<byte[]> Ocsps { get; } = new();

    /// <summary>Errores no fatales encontrados durante el fetch (se registran en logs).</summary>
    public List<string> Warnings { get; } = new();

    public bool IsEmpty => Certificates.Count == 0 && Crls.Count == 0 && Ocsps.Count == 0;
}
