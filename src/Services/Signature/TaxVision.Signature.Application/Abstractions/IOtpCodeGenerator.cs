namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Genera códigos OTP numéricos aleatorios. Se abstrae para poder inyectar generadores
/// deterministas en tests y para permitir ajustes de longitud/entropía por config a
/// futuro.
/// </summary>
public interface IOtpCodeGenerator
{
    /// <summary>
    /// Genera un código OTP de exactamente <paramref name="length"/> dígitos con
    /// entropía criptográfica. Longitud típica: 6.
    /// </summary>
    string Generate(int length);
}
