namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>Clave RSA con la que se firman los enlaces de los firmantes (sección <c>Signature:SignerJwt</c>).</summary>
public sealed class SignerJwtOptions
{
    public const string SectionName = "Signature:SignerJwt";

    /// <summary>Ruta al archivo PEM de la clave privada (en prod, montado desde /run/secrets).</summary>
    public string? PrivateKeyPem { get; set; }
}
