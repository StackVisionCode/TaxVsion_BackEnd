using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>
/// El id que Calendar le manda a Reminder para identificar <b>una ocurrencia</b>.
///
/// <para>
/// Reminder identifica su objetivo con un solo id, y una serie tiene N ocurrencias: un recordatorio
/// por serie dispararía una vez y ya. Así que el id se deriva de la cita más el inicio original de la
/// ocurrencia.
/// </para>
///
/// <para>
/// <b>Determinista a propósito</b>: mover o cancelar una ocurrencia tiene que poder recalcular el
/// mismo id sin guardar un mapa de equivalencias en ningún lado.
/// </para>
/// </summary>
public static class OccurrenceTargetId
{
    /// <summary>Namespace fijo del servicio. Cambiarlo huerfaniza todos los recordatorios vivos.</summary>
    private static readonly Guid CalendarNamespace = new("6f9c1d84-6f3a-4c7e-9a1b-2d5e8f0a3c11");

    public static Guid For(Guid appointmentId, DateTime originalStartUtc)
    {
        var payload = Encoding.UTF8.GetBytes($"{appointmentId:D}|{originalStartUtc.Ticks}");

        Span<byte> seed = stackalloc byte[16 + payload.Length];
        CalendarNamespace.TryWriteBytes(seed);
        payload.CopyTo(seed[16..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(seed, hash);

        // Se marca como UUID v5 para que no se confunda con un Guid aleatorio al leerlo en la base.
        // Los indices son 7 y 8 y no 6 y 8: `new Guid(bytes)` lee los tres primeros grupos en
        // little-endian, asi que el byte de version de la forma canonica es el 7 del arreglo.
        hash[7] = (byte)((hash[7] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash[..16]);
    }
}
