using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Sms.Domain.ValueObjects;

/// <summary>
/// Identificador estable de un destinatario que NO es un cliente (un número escrito a mano). El envío no exige un
/// cliente, pero la fila, el STOP/START y la conversación se agrupan por un Guid: se deriva del número E.164, así el
/// mismo número cae siempre en la misma conversación y su baja (STOP) se puede registrar. Mismo criterio que el
/// destinatario externo de Notification (SHA-256 del teléfono, primeros 16 bytes).
/// </summary>
public static class SmsRecipientId
{
    public static Guid ForPhone(PhoneE164 phone)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(phone.Value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
