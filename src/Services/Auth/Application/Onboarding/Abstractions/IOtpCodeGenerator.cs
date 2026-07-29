namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface IOtpCodeGenerator
{
    /// <summary>Genera un código numérico de 6 dígitos con un generador criptográficamente seguro.</summary>
    string Generate();
}
