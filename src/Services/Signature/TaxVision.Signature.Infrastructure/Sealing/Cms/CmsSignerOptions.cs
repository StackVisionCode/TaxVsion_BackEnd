namespace TaxVision.Signature.Infrastructure.Sealing.Cms;

/// <summary>
/// Credenciales del CMS signer. En dev/staging apunta a un PFX self-signed; en
/// producción se inyecta la ruta a un certificado CA-issued (montaje de secreto).
/// </summary>
public sealed class CmsSignerOptions
{
    public const string SectionName = "Signature:Sealing:Cms";

    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
}
