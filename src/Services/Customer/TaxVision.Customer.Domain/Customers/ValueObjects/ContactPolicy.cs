namespace TaxVision.Customer.Domain.Customers.ValueObjects;

/// <summary>
/// Política de contacto del cliente por canal (opt-out). Decide si el cliente puede recibir
/// notificaciones no-críticas por cada canal; default ambos ON. OTP/transaccionales (p.ej. firma
/// invitada, verificación OTP) siempre salen aunque el flag esté en false. La política se expone
/// al microservicio Notification vía proyección (ver T7.3).
/// </summary>
public sealed record ContactPolicy(bool EmailEnabled, bool SmsEnabled)
{
    public static ContactPolicy Default => new(EmailEnabled: true, SmsEnabled: true);
}
