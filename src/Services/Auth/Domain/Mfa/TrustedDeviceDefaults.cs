namespace TaxVision.Auth.Domain.Mfa;

/// <summary>
/// Valores por defecto de los dispositivos de confianza cuando el tenant no fijó una política
/// propia (<c>TenantMfaPolicy.TrustedDeviceDays</c>).
/// </summary>
public static class TrustedDeviceDefaults
{
    /// <summary>
    /// Días que un dispositivo queda marcado. Vive acá y no como número suelto porque lo aplican
    /// DOS caminos de login —el clásico y el central— y tenían que coincidir.
    /// </summary>
    public const int Days = 30;
}
