namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Canales de verificación de identidad ofrecidos al firmante antes de mostrar el
/// documento. Es un flag bitmask: una solicitud puede activar varios y el firmante
/// elige entre los activos.
///
/// Los canales <see cref="WhatsApp"/>, <see cref="AuthenticatorApp"/> y
/// <see cref="KnowledgeBased"/> están reservados para fases posteriores; su activación
/// es válida sólo cuando el proveedor externo esté cableado.
/// </summary>
[Flags]
public enum VerificationChannel
{
    None = 0,
    Email = 1 << 0,
    Sms = 1 << 1,
    WhatsApp = 1 << 2,
    AuthenticatorApp = 1 << 3,
    PractitionerPin = 1 << 4,
    KnowledgeBased = 1 << 5,
}
