using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Onboarding.Infrastructure.Security;

/// <summary>
/// SOLO DESARROLLO LOCAL: devuelve siempre el mismo código para poder completar el alta en el landing sin
/// depender de que llegue el correo (en local el SMTP es de prueba). El paso de verificación se conserva —el
/// comprador igual escribe el código—, solo deja de ser aleatorio.
/// Nunca se registra por defecto: <see cref="Resolve"/> exige la opción explícita Onboarding:DevFixedOtpCode y se
/// niega a activarse si la resolución de tenants por Host está activa (el modo de producción real).
/// </summary>
public sealed class DevFixedOtpCodeGenerator(string code) : IOtpCodeGenerator
{
    public const string ConfigKey = "Onboarding:DevFixedOtpCode";

    public string Generate() => code;

    /// <summary>
    /// Devuelve el generador a usar. Sin opción → aleatorio. Opción mal formada, o presente con
    /// TenantDomains:EnforceHostResolution=true → falla al arrancar (mejor caer que dejar un OTP fijo en producción).
    /// </summary>
    public static IOtpCodeGenerator Resolve(string? configuredCode, bool enforceHostResolution)
    {
        if (string.IsNullOrWhiteSpace(configuredCode))
            return new NumericOtpCodeGenerator();

        var trimmed = configuredCode.Trim();
        if (trimmed.Length != 6 || !trimmed.All(char.IsAsciiDigit))
            throw new InvalidOperationException($"{ConfigKey} must be exactly 6 digits.");

        if (enforceHostResolution)
            throw new InvalidOperationException(
                $"{ConfigKey} is a local-development shortcut and cannot be enabled while "
                    + "TenantDomains:EnforceHostResolution is true (production mode)."
            );

        return new DevFixedOtpCodeGenerator(trimmed);
    }
}
